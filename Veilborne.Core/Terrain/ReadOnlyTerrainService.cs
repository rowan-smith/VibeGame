using System.Collections.Concurrent;
using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Core;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Objects;
using Vector4 = System.Numerics.Vector4;

namespace Veilborne.Terrain
{
    public class ReadOnlyTerrainService : ITerrainColliderProvider
    {
        public float TileSize { get; } = 2.0f;
        public int ChunkSize { get; } = 64;
        public int MaxConcurrentJobs { get; set; } = 8;

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly EditableTerrainService _editable;
        private readonly ITerrainGenerator _terrainGen;
        private readonly IWorldObjectRenderer _worldObjectRenderer;
        private readonly EntityRegistry _entityRegistry;
        private readonly IWorldConfigService _config;

        // Track loaded chunks and preserve their mesh generation state across frames
        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();
        private readonly Dictionary<(int cx, int cz), List<Entity>> _entitiesByChunk = new();
        private readonly Dictionary<(int cx, int cz), BiomeData> _biomeByChunk = new();
        private readonly Dictionary<(int cx, int cz), (BiomeData? secondary, float blend)> _biomeBlendByChunk = new();

        // Async generation state
        private readonly HashSet<(int cx, int cz)> _generating = new();
        private readonly ConcurrentQueue<((int cx, int cz) key, float[,] heights, Vector2 origin, List<SpawnedObject> objects, BiomeData biome)> _completed = new();
        private int _lastDesiredChunkCount;

        public ReadOnlyTerrainService(EditableTerrainService editable, IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator terrainGen, IWorldObjectRenderer worldObjectRenderer, EntityRegistry entityRegistry, IWorldConfigService config)
        {
            _editable = editable;
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _terrainGen = terrainGen;
            _worldObjectRenderer = worldObjectRenderer;
            _entityRegistry = entityRegistry;
            _config = config;
        }

        public Dictionary<(int cx, int cz), TerrainChunk> GetLoadedChunks() => _loadedChunks;
        public int DesiredChunkCount => _lastDesiredChunkCount;
        public int LoadedChunkCount => _loadedChunks.Count;
        public int GeneratingChunkCount => _generating.Count;
        public int LoadedEntityCount
        {
            get
            {
                int count = 0;
                foreach (var entities in _entitiesByChunk.Values)
                    count += entities.Count;
                return count;
            }
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            // Build a set of desired keys within radius
            var desired = new HashSet<(int cx, int cz)>();
            for (int z = -radiusChunks; z <= radiusChunks; z++)
            for (int x = -radiusChunks; x <= radiusChunks; x++)
            {
                var key = (centerX + x, centerZ + z);
                desired.Add(key);
                if (!_loadedChunks.ContainsKey(key) && !_generating.Contains(key))
                {
                    if (_generating.Count >= System.Math.Max(1, MaxConcurrentJobs))
                        continue;
                    var origin = new Vector2(key.Item1 * ChunkSize * TileSize, key.Item2 * ChunkSize * TileSize);

                    // Off-thread height sampling from editable ring
                    _generating.Add(key);
                    _ = Task.Run(() =>
                    {
                        // Build heights from editable surface so RO ring stays in sync with
                        // editable/LOD sampling and avoids shape pops when transitioning rings.
                        float[,] heights = new float[ChunkSize + 1, ChunkSize + 1];
                        for (int zz = 0; zz <= ChunkSize; zz++)
                        for (int xx = 0; xx <= ChunkSize; xx++)
                        {
                            float wx = origin.X + xx * TileSize;
                            float wz = origin.Y + zz * TileSize;
                            heights[xx, zz] = _editable.SampleHeight(wx, wz);
                        }

                        // Spawn world objects for this chunk using biome spawner
                        var center = new Vector2(
                            origin.X + ChunkSize * TileSize * 0.5f,
                            origin.Y + ChunkSize * TileSize * 0.5f);
                        var (biome, secondaryBiome, secondaryBlend) = ResolveBiomeBlend(center);
                        var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, _terrainGen, heights, origin, 18);
                        _completed.Enqueue((key, heights, origin, raw, biome.Data));
                        lock (_biomeBlendByChunk)
                            _biomeBlendByChunk[key] = (secondaryBiome?.Data, secondaryBlend);
                    });
                }
            }

            // Remove chunks that are no longer within the desired radius
            var toRemove = new List<(int cx, int cz)>();
            foreach (var key in _loadedChunks.Keys)
            {
                if (desired.Contains(key))
                    continue;

                // Hysteresis: keep one extra ring to avoid visible holes while new chunks stream in.
                int dx = Math.Abs(key.cx - centerX);
                int dz = Math.Abs(key.cz - centerZ);
                int chebyshev = Math.Max(dx, dz);
                if (chebyshev <= radiusChunks + 1)
                    continue;

                toRemove.Add(key);
            }
            foreach (var key in toRemove)
            {
                _loadedChunks.Remove(key);
                _biomeByChunk.Remove(key);
                lock (_biomeBlendByChunk)
                    _biomeBlendByChunk.Remove(key);
                if (_entitiesByChunk.TryGetValue(key, out var entities))
                {
                    foreach (var entity in entities)
                        _entityRegistry.DestroyEntity(entity);
                    _entitiesByChunk.Remove(key);
                }
            }

            _lastDesiredChunkCount = desired.Count;
        }

        public async Task PumpAsyncJobs(int maxInstallsPerFrame = int.MaxValue)
        {
            int installs = 0;
            while (_completed.TryDequeue(out var item))
            {
                _generating.Remove(item.key);
                // Install generated heightmap; mesh build remains coordinated by TerrainManager
                _loadedChunks[item.key] = new TerrainChunk
                {
                    Heights = item.heights,
                    BaseHeights = (float[,])item.heights.Clone(),
                    Splatmap = BuildSplatmap(item.heights, item.heights),
                    Origin = item.origin,
                    IsMeshGenerated = false,
                    BuiltFromVersion = -1
                };
                _biomeByChunk[item.key] = item.biome;
                // Create entities for this chunk
                var entities = new List<Entity>();
                if (item.objects != null)
                {
                    foreach (var obj in item.objects)
                    {
                        var entity = _entityRegistry.CreateEntity();
                        entity.AddComponent(new TransformComponent
                        {
                            Position = obj.Position,
                            Rotation = obj.Rotation,
                            Scale = obj.Scale
                        });
                        entity.AddComponent(new RenderComponent
                        {
                            ModelPath = obj.ModelPath,
                            ConfigRotationDegrees = obj.ConfigRotationDegrees
                        });
                        entity.AddComponent(new WorldObjectComponent());
                        entity.AddComponent(new TagComponent { Name = "WorldObject" });
                        entity.AddComponent(new TeamComponent { Id = 0 });
                        entity.AddComponent(new NameComponent { Value = obj.ModelPath });
                        entity.AddComponent(new ParentComponent { EntityId = -1 });
                        entity.AddComponent(new DirtyComponent { NeedsUpdate = false });
                        entity.AddComponent(new LifetimeComponent { RemainingSeconds = 0f });
                        entity.AddComponent(new BillboardComponent { FaceCamera = false });
                        entity.AddComponent(new ShadowCasterComponent { CastsShadows = true });
                        entity.AddComponent(new MaterialComponent { ShaderId = string.Empty, Tint = Vector4.One });
                        entity.AddComponent(new ColliderComponent
                        {
                            Radius = WorldObjectCollisionRules.ComputeColliderRadius(obj)
                        });
                        entity.AddComponent(WorldObjectCollisionRules.GetFilter(obj));
                        entity.AddComponent(new RigidbodyComponent
                        {
                            IsKinematic = true,
                            IsSleeping = false
                        });
                        entity.AddComponent(new TerrainChunkComponent
                        {
                            ChunkX = item.key.Item1,
                            ChunkZ = item.key.Item2,
                            LodLevel = 1
                        });
                        entity.AddComponent(new LodComponent
                        {
                            Level = 1
                        });
                        entity.AddComponent(new BiomeComponent
                        {
                            BiomeId = item.biome.Id
                        });
                        entities.Add(entity);
                    }
                }
                _entitiesByChunk[item.key] = entities;
                installs++;
                if (installs >= maxInstallsPerFrame) break;
                await Task.Yield();
            }
        }

        public void RenderTiles(CameraComponent camera, HashSet<(int cx, int cz)>? exclude = null)
        {
            foreach (var kvp in _loadedChunks)
            {
                var key = kvp.Key;
                var chunk = kvp.Value;

                if (exclude != null && exclude.Contains(key))
                    continue;

                BiomeData primaryBiome;
                if (_biomeByChunk.TryGetValue(key, out var cachedPrimary))
                    primaryBiome = cachedPrimary;
                else
                    primaryBiome = GetDominantBiomeForChunk(chunk).Data;

                (BiomeData? secondary, float blend) blendInfo;
                lock (_biomeBlendByChunk)
                    blendInfo = _biomeBlendByChunk.TryGetValue(key, out var info) ? info : (null, 0f);
                _renderer.ApplyBiomeBlendTextures(primaryBiome, blendInfo.secondary, blendInfo.blend);

                _renderer.RenderAt(
                    chunk.Heights,
                    TileSize,
                    chunk.Origin,
                    camera,
                    chunk.BaseHeights,
                    _config.Config.TerrainLayers,
                    chunk.Splatmap);
            }
        }

        private IBiome GetDominantBiomeForChunk(TerrainChunk chunk)
        {
            var center = new Vector2(
                chunk.Origin.X + ChunkSize * TileSize * 0.5f,
                chunk.Origin.Y + ChunkSize * TileSize * 0.5f);
            return ResolveBiomeBlend(center).primary;
        }

        private (IBiome primary, IBiome? secondary, float blend) ResolveBiomeBlend(Vector2 centerWorld)
        {
            if (_biomeProvider is SimpleBiomeProvider simple)
                return simple.GetBiomeBlendAt(centerWorld, _terrainGen);

            var primary = _biomeProvider.GetBiomeAt(centerWorld, _terrainGen);
            return (primary, null, 0f);
        }

        public void Render(CameraComponent camera) => RenderTiles(camera);

        public void RenderWithExclusions(CameraComponent camera, HashSet<(int cx, int cz)> exclusions)
            => RenderTiles(camera, exclusions);

        public float SampleHeight(float worldX, float worldZ)
            => _editable.SampleHeight(worldX, worldZ);

        public IBiome GetBiomeAt(float worldX, float worldZ)
            => _biomeProvider.GetBiomeAt(new Vector2(worldX, worldZ), null);

        public void RenderDebugChunkBounds(CameraComponent camera)
        {
            // Debug chunk bounds are drawn by VeilborneEngine as projected 2D overlays.
        }

        public IEnumerable<(Vector3 center, Vector3 size)> EnumerateChunkBounds()
        {
            foreach (var chunk in _loadedChunks.Values)
            {
                float worldSize = ChunkSize * TileSize;
                yield return (
                    new Vector3(chunk.Origin.X + worldSize * 0.5f, 0f, chunk.Origin.Y + worldSize * 0.5f),
                    new Vector3(worldSize, 2f, worldSize));
            }
        }

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            yield return (worldPos + new Vector2(10, 0), 5);
            yield return (worldPos + new Vector2(-8, 7), 3);
        }

        private Vector4[,] BuildSplatmap(float[,] heights, float[,]? baseHeights)
        {
            int w = heights.GetLength(0);
            int h = heights.GetLength(1);
            var splat = new Vector4[w, h];
            var layers = _config.Config.TerrainLayers;
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float depth = 0f;
                if (baseHeights != null)
                    depth = MathF.Max(0f, baseHeights[x, z] - heights[x, z]);

                float top = Math.Clamp(1f - depth / MathF.Max(0.05f, layers.SubsurfaceDepth), 0f, 1f);
                float dirt = 0f;
                float rock = 0f;
                if (depth > 0f)
                {
                    float subT = Math.Clamp(depth / MathF.Max(0.05f, layers.SubsurfaceDepth), 0f, 1f);
                    float deepT = Math.Clamp((depth - layers.SubsurfaceDepth) / MathF.Max(0.05f, layers.DeepDepth - layers.SubsurfaceDepth), 0f, 1f);
                    dirt = Math.Clamp(subT * (1f - deepT), 0f, 1f);
                    rock = deepT;
                }
                float sum = top + dirt + rock;
                splat[x, z] = sum > 1e-5f
                    ? new Vector4(top / sum, dirt / sum, rock / sum, 0f)
                    : new Vector4(1f, 0f, 0f, 0f);
            }
            return splat;
        }
    }
}
