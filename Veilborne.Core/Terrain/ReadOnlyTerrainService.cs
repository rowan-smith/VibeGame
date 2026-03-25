using System.Collections.Concurrent;
using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Objects;

namespace Veilborne.Terrain
{
    public class ReadOnlyTerrainService : ITerrainColliderProvider
    {
        public float TileSize { get; } = 2.0f;
        public int ChunkSize { get; } = 64;

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly EditableTerrainService _editable;
        private readonly ITerrainGenerator _terrainGen;
        private readonly IWorldObjectRenderer _worldObjectRenderer;
        private readonly EntityRegistry _entityRegistry;

        // Track loaded chunks and preserve their mesh generation state across frames
        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();
        private readonly Dictionary<(int cx, int cz), List<Entity>> _entitiesByChunk = new();
        private readonly Dictionary<(int cx, int cz), BiomeData> _biomeByChunk = new();

        // Async generation state
        private readonly HashSet<(int cx, int cz)> _generating = new();
        private readonly ConcurrentQueue<((int cx, int cz) key, float[,] heights, Vector2 origin, List<SpawnedObject> objects, BiomeData biome)> _completed = new();
        private int _lastDesiredChunkCount;

        public ReadOnlyTerrainService(EditableTerrainService editable, IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator terrainGen, IWorldObjectRenderer worldObjectRenderer, EntityRegistry entityRegistry)
        {
            _editable = editable;
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _terrainGen = terrainGen;
            _worldObjectRenderer = worldObjectRenderer;
            _entityRegistry = entityRegistry;
        }

        public Dictionary<(int cx, int cz), TerrainChunk> GetLoadedChunks() => _loadedChunks;
        public int DesiredChunkCount => _lastDesiredChunkCount;
        public int LoadedChunkCount => _loadedChunks.Count;
        public int GeneratingChunkCount => _generating.Count;

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
                        var biome = BiomeSampling.GetDominantBiomeNearPoint(_biomeProvider, null, center, 96f, 11, 2f);
                        var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, _terrainGen, heights, origin, 18);
                        _completed.Enqueue((key, heights, origin, raw, biome.Data));
                    });
                }
            }

            // Remove chunks that are no longer within the desired radius
            var toRemove = new List<(int cx, int cz)>();
            foreach (var key in _loadedChunks.Keys)
                if (!desired.Contains(key)) toRemove.Add(key);
            foreach (var key in toRemove)
            {
                _loadedChunks.Remove(key);
                _biomeByChunk.Remove(key);
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
                        entity.AddComponent(new PhysicsComponent
                        {
                            CollisionRadius = obj.CollisionRadius,
                            IsStatic = true
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

                if (_biomeByChunk.TryGetValue(key, out var primaryBiome))
                    _renderer.ApplyBiomeTextures(primaryBiome);
                else
                    _renderer.ApplyBiomeTextures(GetDominantBiomeForChunk(chunk).Data);

                // Render the chunk (meshes are built centrally by TerrainManager.UpdateAround)
                _renderer.RenderAt(chunk.Heights, TileSize, chunk.Origin, camera);
            }
        }

        private IBiome GetDominantBiomeForChunk(TerrainChunk chunk)
        {
            var center = new Vector2(
                chunk.Origin.X + ChunkSize * TileSize * 0.5f,
                chunk.Origin.Y + ChunkSize * TileSize * 0.5f);
            return BiomeSampling.GetDominantBiomeNearPoint(_biomeProvider, null, center, 96f, 11, 2f);
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
            // Debug chunk bounds are backend-specific and currently disabled.
        }

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            yield return (worldPos + new Vector2(10, 0), 5);
            yield return (worldPos + new Vector2(-8, 7), 3);
        }
    }
}
