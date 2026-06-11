using System.Collections.Concurrent;
using System.Linq;
using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
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
        private readonly HashSet<(int cx, int cz)> _desiredKeysScratch = new();
        private readonly List<(int cx, int cz)> _toRemoveScratch = new();
        private readonly List<KeyValuePair<(int cx, int cz), TerrainChunk>> _pairSnapshotBuffer = new();
        private readonly List<TerrainChunk> _chunkSnapshotBuffer = new();
        private readonly Queue<(int cx, int cz)> _pendingSpawnOrder = new();
        private readonly Dictionary<(int cx, int cz), PendingSpawnBatch> _pendingSpawnsByChunk = new();
        private const int MaxObjectSpawnsPerFrame = 32;

        // Async generation state
        private readonly HashSet<(int cx, int cz)> _generating = new();
        private readonly ConcurrentQueue<((int cx, int cz) key, float[,] heights, Vector2 origin, List<SpawnedObject> objects, BiomeData biome)> _completed = new();
        private int _lastDesiredChunkCount;

        private sealed class PendingSpawnBatch
        {
            public required List<SpawnedObject> Objects { get; init; }
            public required BiomeData Biome { get; init; }
            public int Cursor { get; set; }
        }

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
            get => SumLoadedEntitiesSafe(_entitiesByChunk);
        }
        public int PendingSpawnObjectCount
        {
            get => SumPendingSpawnsSafe(_pendingSpawnsByChunk);
        }

        private static int SumLoadedEntitiesSafe(Dictionary<(int cx, int cz), List<Entity>> entitiesByChunk)
        {
            while (true)
            {
                try
                {
                    int count = 0;
                    foreach (var entities in entitiesByChunk.Values.ToArray())
                        count += entities.Count;
                    return count;
                }
                catch (InvalidOperationException)
                {
                    // Collection changed while snapshotting; retry.
                }
                catch (ArgumentException)
                {
                    // Collection resized while copying; retry.
                }
            }
        }

        private static int SumPendingSpawnsSafe(Dictionary<(int cx, int cz), PendingSpawnBatch> pendingByChunk)
        {
            while (true)
            {
                try
                {
                    int pending = 0;
                    foreach (var batch in pendingByChunk.Values.ToArray())
                        pending += Math.Max(0, batch.Objects.Count - batch.Cursor);
                    return pending;
                }
                catch (InvalidOperationException)
                {
                    // Collection changed while snapshotting; retry.
                }
                catch (ArgumentException)
                {
                    // Collection resized while copying; retry.
                }
            }
        }

        private static (int cx, int cz)[] SnapshotKeysSafe(Dictionary<(int cx, int cz), TerrainChunk> chunks)
        {
            while (true)
            {
                try
                {
                    return chunks.Keys.ToArray();
                }
                catch (InvalidOperationException)
                {
                    // Collection changed while snapshotting; retry.
                }
                catch (ArgumentException)
                {
                    // Collection resized while copying; retry.
                }
            }
        }

        private List<KeyValuePair<(int cx, int cz), TerrainChunk>> SnapshotPairsSafe(Dictionary<(int cx, int cz), TerrainChunk> chunks)
        {
            _pairSnapshotBuffer.Clear();
            while (true)
            {
                try
                {
                    _pairSnapshotBuffer.AddRange(chunks);
                    return _pairSnapshotBuffer;
                }
                catch (InvalidOperationException)
                {
                    _pairSnapshotBuffer.Clear();
                }
                catch (ArgumentException)
                {
                    _pairSnapshotBuffer.Clear();
                }
            }
        }

        private List<TerrainChunk> SnapshotChunksSafe(Dictionary<(int cx, int cz), TerrainChunk> chunks)
        {
            _chunkSnapshotBuffer.Clear();
            while (true)
            {
                try
                {
                    _chunkSnapshotBuffer.AddRange(chunks.Values);
                    return _chunkSnapshotBuffer;
                }
                catch (InvalidOperationException)
                {
                    _chunkSnapshotBuffer.Clear();
                }
                catch (ArgumentException)
                {
                    _chunkSnapshotBuffer.Clear();
                }
            }
        }

        private readonly Dictionary<(int cx, int cz), BiomeData> _biomeSnapshotBuffer = new();

        private Dictionary<(int cx, int cz), BiomeData> SnapshotBiomesSafe(Dictionary<(int cx, int cz), BiomeData> biomesByChunk)
        {
            _biomeSnapshotBuffer.Clear();
            while (true)
            {
                try
                {
                    foreach (var kv in biomesByChunk)
                        _biomeSnapshotBuffer[kv.Key] = kv.Value;
                    return _biomeSnapshotBuffer;
                }
                catch (InvalidOperationException)
                {
                    _biomeSnapshotBuffer.Clear();
                }
                catch (ArgumentException)
                {
                    _biomeSnapshotBuffer.Clear();
                }
            }
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            // Build a set of desired keys within radius
            _desiredKeysScratch.Clear();
            for (int z = -radiusChunks; z <= radiusChunks; z++)
            for (int x = -radiusChunks; x <= radiusChunks; x++)
            {
                var key = (centerX + x, centerZ + z);
                _desiredKeysScratch.Add(key);
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
                        var (biome, secondaryBiome, secondaryBlend) = ResolveVisualBiome(origin);
                        var raw = biome.ObjectSpawner.GenerateObjects(biome.Id, _terrainGen, heights, origin, 18);
                        _completed.Enqueue((key, heights, origin, raw, biome.Data));
                        lock (_biomeBlendByChunk)
                            _biomeBlendByChunk[key] = (secondaryBiome?.Data, secondaryBlend);
                    });
                }
            }

            // Remove chunks that are no longer within the desired radius
            _toRemoveScratch.Clear();
            var loadedKeysSnapshot = SnapshotKeysSafe(_loadedChunks);
            foreach (var key in loadedKeysSnapshot)
            {
                if (_desiredKeysScratch.Contains(key))
                    continue;

                // Hysteresis: keep one extra ring to avoid visible holes while new chunks stream in.
                int dx = Math.Abs(key.cx - centerX);
                int dz = Math.Abs(key.cz - centerZ);
                int chebyshev = Math.Max(dx, dz);
                if (chebyshev <= radiusChunks + 1)
                    continue;

                _toRemoveScratch.Add(key);
            }
            foreach (var key in _toRemoveScratch)
            {
                _loadedChunks.Remove(key);
                _biomeByChunk.Remove(key);
                _pendingSpawnsByChunk.Remove(key);
                lock (_biomeBlendByChunk)
                    _biomeBlendByChunk.Remove(key);
                if (_entitiesByChunk.TryGetValue(key, out var entities))
                {
                    foreach (var entity in entities)
                        _entityRegistry.DestroyEntity(entity);
                    _entitiesByChunk.Remove(key);
                }
            }

            _lastDesiredChunkCount = _desiredKeysScratch.Count;
        }

        public async Task PumpAsyncJobs(int maxInstallsPerFrame = int.MaxValue, bool warmupMode = false)
        {
            int installs = 0;
            while (_completed.TryDequeue(out var item))
            {
                _generating.Remove(item.key);
                // Look up blend info for this chunk
                BiomeData? secBiome = null;
                float secBlend = 0f;
                lock (_biomeBlendByChunk)
                {
                    if (_biomeBlendByChunk.TryGetValue(item.key, out var bi))
                    { secBiome = bi.secondary; secBlend = bi.blend; }
                }
                // Install generated heightmap; mesh build remains coordinated by TerrainManager
                _loadedChunks[item.key] = new TerrainChunk
                {
                    Heights = item.heights,
                    BaseHeights = (float[,])item.heights.Clone(),
                    Splatmap = BuildSplatmap(item.heights, item.heights, item.biome, secBiome, secBlend, TileSize),
                    Origin = item.origin,
                    IsMeshGenerated = false,
                    BuiltFromVersion = -1
                };
                _biomeByChunk[item.key] = item.biome;
                _entitiesByChunk[item.key] = new List<Entity>(item.objects?.Count ?? 0);
                if (item.objects is { Count: > 0 })
                {
                    _pendingSpawnsByChunk[item.key] = new PendingSpawnBatch
                    {
                        Objects = item.objects,
                        Biome = item.biome,
                        Cursor = 0
                    };
                    _pendingSpawnOrder.Enqueue(item.key);
                }
                installs++;
                if (installs >= maxInstallsPerFrame) break;
                await Task.Yield();
            }

            int spawnBudget = warmupMode ? int.MaxValue : MaxObjectSpawnsPerFrame;
            ProcessPendingObjectSpawns(spawnBudget);
        }

        private void ProcessPendingObjectSpawns(int budget)
        {
            int remaining = Math.Max(0, budget);
            while (remaining > 0 && _pendingSpawnOrder.Count > 0)
            {
                var key = _pendingSpawnOrder.Dequeue();
                if (!_pendingSpawnsByChunk.TryGetValue(key, out var batch))
                    continue;
                if (!_entitiesByChunk.TryGetValue(key, out var entities))
                {
                    _pendingSpawnsByChunk.Remove(key);
                    continue;
                }

                int spawnCount = Math.Min(8, remaining);
                while (spawnCount-- > 0 && batch.Cursor < batch.Objects.Count)
                {
                    var obj = batch.Objects[batch.Cursor++];
                    entities.Add(CreateWorldObjectEntity(obj, key, 1, batch.Biome.Id));
                    remaining--;
                }

                if (batch.Cursor < batch.Objects.Count)
                    _pendingSpawnOrder.Enqueue(key);
                else
                    _pendingSpawnsByChunk.Remove(key);
            }
        }

        private Entity CreateWorldObjectEntity(SpawnedObject obj, (int cx, int cz) key, int lodLevel, string biomeId)
        {
            var entity = _entityRegistry.CreateEntity();
            float groundedY = _editable.SampleHeight(obj.Position.X, obj.Position.Z);
            bool isFoliage = WorldObjectCollisionRules.IsFoliage(obj);
            entity.AddComponent(new TransformComponent
            {
                Position = new Vector3(obj.Position.X, groundedY, obj.Position.Z),
                Rotation = obj.Rotation,
                Scale = obj.Scale
            });
            entity.AddComponent(new RenderComponent
            {
                ModelPath = obj.ModelPath,
                IsFoliage = isFoliage
            });
            entity.AddComponent(new WorldObjectComponent());
            entity.AddComponent(new TagComponent { Name = "WorldObject" });
            entity.AddComponent(new NameComponent
            {
                Value = !string.IsNullOrWhiteSpace(obj.ObjectDisplayName)
                    ? obj.ObjectDisplayName
                    : (string.IsNullOrWhiteSpace(obj.ObjectId) ? obj.ModelPath : obj.ObjectId)
            });
            entity.AddComponent(new ParentComponent { EntityId = -1 });
            entity.AddComponent(new DirtyComponent { NeedsUpdate = false });
            entity.AddComponent(new ShadowCasterComponent { CastsShadows = !isFoliage });
            entity.AddComponent(new MaterialComponent { ShaderId = string.Empty, Tint = Vector4.One });
            float colliderRadius = WorldObjectCollisionRules.ComputeColliderRadius(obj);
            if (colliderRadius > 0f)
            {
                entity.AddComponent(new ColliderComponent
                {
                    Radius = colliderRadius
                });
                entity.AddComponent(WorldObjectCollisionRules.GetFilter(obj));
                entity.AddComponent(new RigidbodyComponent
                {
                    IsKinematic = true,
                    IsSleeping = false
                });
            }
            entity.AddComponent(new TerrainChunkComponent
            {
                ChunkX = key.Item1,
                ChunkZ = key.Item2,
                LodLevel = lodLevel
            });
            entity.AddComponent(new BiomeComponent
            {
                BiomeId = biomeId
            });
            return entity;
        }

        public void RenderTiles(CameraComponent camera, HashSet<(int cx, int cz)>? exclude = null)
        {
            var loadedPairs = SnapshotPairsSafe(_loadedChunks);
            foreach (var kvp in loadedPairs)
            {
                var key = kvp.Key;
                var chunk = kvp.Value;

                if (exclude != null && exclude.Contains(key))
                    continue;

                var (primaryIbiome, secondaryIbiome, secondaryBlend) = ResolveVisualBiome(chunk.Origin);
                BiomeData primaryBiome = primaryIbiome.Data;
                (BiomeData? secondary, float blend) blendInfo = (null, 0f);
                if (secondaryIbiome is not null && secondaryBlend > 0.001f)
                    blendInfo = (secondaryIbiome.Data, secondaryBlend);
                _renderer.ApplyBiomeBlendTextures(primaryBiome, blendInfo.secondary, blendInfo.blend);

                _renderer.RenderAt(
                    chunk.Heights,
                    TileSize,
                    chunk.Origin,
                    camera,
                    chunk.BaseHeights,
                    primaryBiome.TerrainLayers,
                    chunk.Splatmap);
            }
        }

        private (IBiome primary, IBiome? secondary, float blend) ResolveVisualBiome(Vector2 chunkOrigin)
            => BiomeSampling.ResolveVisualBiomeForChunk(_biomeProvider, _terrainGen, chunkOrigin, ChunkSize, TileSize);

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
            var chunks = SnapshotChunksSafe(_loadedChunks);
            foreach (var chunk in chunks)
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

        private Vector4[,] BuildSplatmap(float[,] heights, float[,]? baseHeights, BiomeData biome,
            BiomeData? secondaryBiome = null, float blendFactor = 0f, float tileSize = 1f)
        {
            int w = heights.GetLength(0);
            int h = heights.GetLength(1);
            var splat = new Vector4[w, h];
            float spacing = MathF.Max(0.01f, tileSize);

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float depth = 0f;
                if (baseHeights != null)
                    depth = MathF.Max(0f, baseHeights[x, z] - heights[x, z]);

                float slope = ComputeSlopeAt(heights, x, z, w, h, spacing);

                splat[x, z] = ComputeSplatForLayers(biome.TerrainLayers, depth, slope);
            }

            // Multi-biome per-vertex blend overlay: sample biome blend weights at a grid
            // and interpolate splatmap where secondary biomes have influence.
            if (_biomeProvider is SimpleBiomeProvider && w > 1 && h > 1)
            {
                for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    float depth = 0f;
                    if (baseHeights != null)
                        depth = MathF.Max(0f, baseHeights[x, z] - heights[x, z]);
                    float slopeVal = ComputeSlopeAt(heights, x, z, w, h, spacing);

                    if (secondaryBiome == null || blendFactor <= 0.001f)
                        continue;

                    // Spatially-varying blend using smooth gradient across chunk
                    float tx = x / (float)(w - 1);
                    float tz = z / (float)(h - 1);
                    float edgeFade = MathF.Max(
                        MathF.Abs(tx - 0.5f) * 2f,
                        MathF.Abs(tz - 0.5f) * 2f);
                    float vertexBlend = blendFactor * (0.3f + 0.7f * edgeFade);
                    vertexBlend = vertexBlend * vertexBlend * (3f - 2f * vertexBlend); // smoothstep

                    if (vertexBlend > 0.005f)
                    {
                        Vector4 secondary = ComputeSplatForLayers(secondaryBiome.TerrainLayers, depth, slopeVal);
                        splat[x, z] = Vector4.Lerp(splat[x, z], secondary, vertexBlend);
                    }
                }
            }

            return splat;
        }

        private static float ComputeSlopeAt(float[,] heights, int x, int z, int w, int h, float spacing)
        {
            float center = heights[x, z];
            float left  = x > 0 ? heights[x - 1, z] : center;
            float right = x < w - 1 ? heights[x + 1, z] : center;
            float up    = z > 0 ? heights[x, z - 1] : center;
            float down  = z < h - 1 ? heights[x, z + 1] : center;
            float dx = (right - left) / (2f * spacing);
            float dz = (down - up) / (2f * spacing);
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static Vector4 ComputeSplatForLayers(TerrainLayerConfig layers, float depth, float slope)
        {
            float slopeThreshold = layers.SlopeRockThreshold;
            float slopeRange = MathF.Max(0.01f, layers.SlopeBlendRange);

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

            float slopeFactor = Math.Clamp((slope - slopeThreshold) / slopeRange, 0f, 1f);
            if (slopeFactor > 0f)
            {
                rock = MathF.Min(1f, rock + slopeFactor * top * 0.7f + slopeFactor * dirt * 0.3f);
                top *= 1f - slopeFactor * 0.7f;
                dirt *= 1f - slopeFactor * 0.3f;
            }

            float sum = top + dirt + rock;
            return sum > 1e-5f
                ? new Vector4(top / sum, dirt / sum, rock / sum, 0f)
                : new Vector4(1f, 0f, 0f, 0f);
        }
    }
}
