using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Veilborne.Interfaces;
using Veilborne.Objects;
using Veilborne.Biomes;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;

namespace Veilborne.Terrain
{
    public class EditableTerrainService
    {
        public bool RenderBaseHeightmap { get; set; } = true;

        public float TileSize { get; } = 1.0f;
        public int ChunkSize { get; } = 32;

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly ITerrainGenerator _terrainGen;
        private readonly IWorldObjectRenderer _worldObjectRenderer;
        private readonly EntityRegistry _entityRegistry;
        private readonly IWorldConfigService _config;

        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();
        private readonly Dictionary<(int cx, int cz), List<Entity>> _entitiesByChunk = new();
        private readonly Dictionary<(int cx, int cz), BiomeData> _primaryBiomeByChunk = new();
        private readonly Dictionary<(int cx, int cz), (BiomeData? secondary, float blend)> _biomeBlendByChunk = new();
        private readonly Dictionary<(int cx, int cz), (BiomeData? merge, float maxMerge)> _renderMergeByChunk = new();
        private readonly Dictionary<string, BiomeData> _biomeConfigById = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<(int cx, int cz)> _desiredKeysScratch = new();
        private readonly List<(int cx, int cz)> _toRemoveScratch = new();
        private readonly Queue<(int cx, int cz)> _pendingSpawnOrder = new();
        private readonly Dictionary<(int cx, int cz), PendingSpawnBatch> _pendingSpawnsByChunk = new();
        private readonly Queue<TerrainEditCommand> _pendingTerrainEdits = new();
        private readonly HashSet<(int cx, int cz)> _generating = new();
        private readonly ConcurrentQueue<GeneratedEditableChunk> _completed = new();
        private const int MaxObjectSpawnsPerFrame = 24;
        private const int MaxTerrainEditsQueued = 16;
        private const int MaxEditableChunkInstallsPerFrame = 1;
        private const int MaxEditableConcurrentJobs = 2;
        private const int MaxEditableConcurrentJobsWarmup = 12;
        private readonly object _lock = new();
        private int _lastDesiredChunkCount;
        private bool _isWarmupMode;
        private int _lastCenterChunkX;
        private int _lastCenterChunkZ;
        private int _lastRadiusChunks;

        private sealed class PendingSpawnBatch
        {
            public required List<SpawnedObject> Objects { get; init; }
            public required string BiomeId { get; init; }
            public int Cursor { get; set; }
        }

        private enum TerrainEditKind
        {
            Dig,
            Place
        }

        private readonly record struct TerrainEditCommand(
            TerrainEditKind Kind,
            Vector3 Center,
            float Radius,
            float Strength,
            VoxelFalloff Falloff);

        private readonly record struct GeneratedEditableChunk(
            (int cx, int cz) Key,
            float[,] Heights,
            Vector2 Origin,
            BiomeData PrimaryBiome,
            BiomeData? SecondaryBiome,
            float SecondaryBlend,
            List<SpawnedObject> Objects);

        public EditableTerrainService(IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator terrainGen, IWorldObjectRenderer worldObjectRenderer, EntityRegistry entityRegistry, IWorldConfigService config)
        {
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
        public int PendingSpawnObjectCount
        {
            get
            {
                lock (_lock)
                {
                    int pending = 0;
                    foreach (var batch in _pendingSpawnsByChunk.Values)
                    {
                        if (batch?.Objects is not { } objects)
                            continue;
                        pending += Math.Max(0, objects.Count - batch.Cursor);
                    }
                    return pending;
                }
            }
        }
        public int LoadedEntityCount
        {
            get => SumLoadedEntitiesSafe(_entitiesByChunk);
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

        private (int cx, int cz) WorldToChunkKey(float worldX, float worldZ)
        {
            int cx = (int)MathF.Floor(worldX / (ChunkSize * TileSize));
            int cz = (int)MathF.Floor(worldZ / (ChunkSize * TileSize));
            return (cx, cz);
        }

        private bool TryGetLocalIndex((int cx, int cz) key, float worldX, float worldZ, out int ix, out int iz)
        {
            ix = iz = 0;
            if (!_loadedChunks.TryGetValue(key, out var chunk)) return false;
            float localX = (worldX - chunk.Origin.X) / TileSize;
            float localZ = (worldZ - chunk.Origin.Y) / TileSize;
            ix = Math.Clamp((int)MathF.Round(localX), 0, ChunkSize);
            iz = Math.Clamp((int)MathF.Round(localZ), 0, ChunkSize);
            return true;
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            lock (_lock)
            {
                _lastCenterChunkX = centerX;
                _lastCenterChunkZ = centerZ;
                _lastRadiusChunks = radiusChunks;
                _desiredKeysScratch.Clear();
                var nearby = TerrainChunkSpatialHash.GetChunksAround(centerX, centerZ, radiusChunks);
                for (int i = 0; i < nearby.Length; i++)
                {
                    var key = nearby[i];
                    _desiredKeysScratch.Add(key);
                    if (_loadedChunks.ContainsKey(key) || _generating.Contains(key))
                        continue;

                    int maxConcurrent = _isWarmupMode ? MaxEditableConcurrentJobsWarmup : MaxEditableConcurrentJobs;
                    if (_generating.Count >= maxConcurrent)
                        continue;

                    _generating.Add(key);
                    QueueChunkGeneration(key);
                }

                // unload chunks no longer desired
                _toRemoveScratch.Clear();
                foreach (var key in _loadedChunks.Keys)
                    if (!_desiredKeysScratch.Contains(key)) _toRemoveScratch.Add(key);
                foreach (var key in _toRemoveScratch)
                {
                    _loadedChunks.Remove(key);
                    _primaryBiomeByChunk.Remove(key);
                    _renderMergeByChunk.Remove(key);
                    _biomeBlendByChunk.Remove(key);
                    _pendingSpawnsByChunk.Remove(key);
                    if (_entitiesByChunk.TryGetValue(key, out var entities))
                    {
                        foreach (var entity in entities)
                            _entityRegistry.DestroyEntity(entity);
                        _entitiesByChunk.Remove(key);
                    }
                }

                _lastDesiredChunkCount = _desiredKeysScratch.Count;
            }
        }

        private void QueueChunkGeneration((int cx, int cz) key)
        {
            _ = Task.Run(() =>
            {
                float originX = key.cx * ChunkSize * TileSize;
                float originZ = key.cz * ChunkSize * TileSize;
                var origin = new Vector2(originX, originZ);
                float[,] heights = new float[ChunkSize + 1, ChunkSize + 1];
                for (int z = 0; z <= ChunkSize; z++)
                for (int x = 0; x <= ChunkSize; x++)
                {
                    float wx = originX + x * TileSize;
                    float wz = originZ + z * TileSize;
                    float baseHeight = _terrainGen.ComputeHeight(wx, wz);
                    heights[x, z] = BiomeTerrainHeightBlender.ComputeHeight(wx, wz, baseHeight, _biomeProvider, _terrainGen);
                }

                var center = new Vector2(
                    originX + ChunkSize * TileSize * 0.5f,
                    originZ + ChunkSize * TileSize * 0.5f);
                var (primaryBiome, secondaryBiome, secondaryBlend) = ResolveBiomeBlend(center);
                var spawnBiome = _biomeProvider.GetBiomeAt(center, _terrainGen);
                var objects = spawnBiome.ObjectSpawner.GenerateObjects(spawnBiome.Id, _terrainGen, heights, origin, 18);
                _completed.Enqueue(new GeneratedEditableChunk(key, heights, origin, primaryBiome, secondaryBiome, secondaryBlend, objects));
            });
        }

        private void InstallCompletedChunks(int maxInstalls)
        {
            int installs = 0;
            while (installs < maxInstalls && _completed.TryDequeue(out var result))
            {
                _generating.Remove(result.Key);
                int dx = Math.Abs(result.Key.cx - _lastCenterChunkX);
                int dz = Math.Abs(result.Key.cz - _lastCenterChunkZ);
                if (Math.Max(dx, dz) > _lastRadiusChunks + 1)
                    continue;
                if (_loadedChunks.ContainsKey(result.Key))
                    continue;

                var chunk = new TerrainChunk
                {
                    Heights = result.Heights,
                    BaseHeights = (float[,])result.Heights.Clone(),
                    Origin = result.Origin,
                    IsMeshGenerated = false,
                    Dirty = true,
                    Version = 0,
                    BuiltFromVersion = -1
                };
                _loadedChunks[result.Key] = chunk;
                _primaryBiomeByChunk[result.Key] = result.PrimaryBiome;
                RegisterBiomeConfig(result.PrimaryBiome);
                if (result.SecondaryBiome is not null)
                    RegisterBiomeConfig(result.SecondaryBiome);
                _biomeBlendByChunk[result.Key] = (result.SecondaryBiome, result.SecondaryBlend);

                var newChunk = _loadedChunks[result.Key];
                InitializeChunkLayersAndResources(result.Key, ref newChunk, result.PrimaryBiome.Id);
                RecomputeSplatmapForChunk(result.Key, ref newChunk);
                _loadedChunks[result.Key] = newChunk;

                _entitiesByChunk[result.Key] = new List<Entity>(result.Objects?.Count ?? 0);
                if (result.Objects is { Count: > 0 })
                {
                    _pendingSpawnsByChunk[result.Key] = new PendingSpawnBatch
                    {
                        Objects = result.Objects,
                        BiomeId = result.PrimaryBiome.Id,
                        Cursor = 0
                    };
                    _pendingSpawnOrder.Enqueue(result.Key);
                }

                installs++;
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            // If this position is in a loaded editable chunk, bilinearly sample from its heightmap (includes edits).
            // Use TryEnter to avoid blocking the render/probe thread when PumpAsyncJobs holds the lock.
            var key = WorldToChunkKey(worldX, worldZ);
            bool lockTaken = false;
            try
            {
                lockTaken = System.Threading.Monitor.TryEnter(_lock, 0);
                if (lockTaken && _loadedChunks.TryGetValue(key, out var chunk))
                {
                    return SampleFromChunk(chunk, worldX, worldZ);
                }
            }
            finally
            {
                if (lockTaken)
                    System.Threading.Monitor.Exit(_lock);
            }

            // Fallback to procedural world height when this editable chunk isn't loaded or lock is contended.
            // Blend biome influences in world-space to keep transitions organic and decoupled from chunk bounds.
            float baseHeight = _terrainGen.ComputeHeight(worldX, worldZ);
            return BiomeTerrainHeightBlender.ComputeHeight(worldX, worldZ, baseHeight, _biomeProvider, _terrainGen);
        }

        private float SampleFromChunk(TerrainChunk chunk, float worldX, float worldZ)
        {
            // Bilinear interpolation of the chunk heightmap at arbitrary world coordinates
            float localX = (worldX - chunk.Origin.X) / TileSize;
            float localZ = (worldZ - chunk.Origin.Y) / TileSize;

            int x0 = Math.Clamp((int)MathF.Floor(localX), 0, ChunkSize);
            int z0 = Math.Clamp((int)MathF.Floor(localZ), 0, ChunkSize);
            int x1 = Math.Clamp(x0 + 1, 0, ChunkSize);
            int z1 = Math.Clamp(z0 + 1, 0, ChunkSize);

            float tx = Math.Clamp(localX - x0, 0f, 1f);
            float tz = Math.Clamp(localZ - z0, 0f, 1f);

            float h00 = chunk.Heights[x0, z0];
            float h10 = chunk.Heights[x1, z0];
            float h01 = chunk.Heights[x0, z1];
            float h11 = chunk.Heights[x1, z1];

            float hx0 = Lerp(h00, h10, tx);
            float hx1 = Lerp(h01, h11, tx);
            return Lerp(hx0, hx1, tz);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private (BiomeData primary, BiomeData? secondary, float blend) ResolveBiomeBlend(Vector2 centerWorld)
        {
            if (_biomeProvider is SimpleBiomeProvider simple)
            {
                var (primary, secondary, blend) = simple.GetBiomeBlendAt(centerWorld, _terrainGen);
                return (primary.Data, secondary?.Data, blend);
            }

            var primaryBiome = _biomeProvider.GetBiomeAt(centerWorld, _terrainGen);
            return (primaryBiome.Data, null, 0f);
        }

        private BiomeData ResolveChunkPrimaryBiome(TerrainChunk chunk)
        {
            int gridW = chunk.Heights.GetLength(0);
            int gridH = chunk.Heights.GetLength(1);
            if (_biomeProvider is SimpleBiomeProvider simple)
            {
                var (primaryId, _, _) = BiomeSampling.ResolveChunkBiomePair(
                    simple, _terrainGen, chunk.Origin, gridW, gridH, TileSize, 4f);
                if (simple.TryGetBiomeById(primaryId, out var primary) && primary is not null)
                    return primary.Data;
            }

            var center = new Vector2(
                chunk.Origin.X + ChunkSize * TileSize * 0.5f,
                chunk.Origin.Y + ChunkSize * TileSize * 0.5f);
            var (fallback, secondary, blend) = ResolveBiomeBlend(center);
            var key = (
                (int)MathF.Floor(chunk.Origin.X / (ChunkSize * TileSize)),
                (int)MathF.Floor(chunk.Origin.Y / (ChunkSize * TileSize)));
            _biomeBlendByChunk[key] = (secondary, blend);
            return fallback;
        }

        private (BiomeData? mergeBiome, float maxMerge) ResolveChunkRenderMerge(Vector2 origin, int gridWidth, int gridHeight)
        {
            if (_biomeProvider is SimpleBiomeProvider simple)
            {
                var (_, mergeId, maxMerge) = BiomeSampling.ResolveChunkBiomePair(
                    simple, _terrainGen, origin, gridWidth, gridHeight, TileSize, 4f);
                if (!string.IsNullOrEmpty(mergeId) &&
                    simple.TryGetBiomeById(mergeId, out var mergeBiome) &&
                    mergeBiome is not null)
                    return (mergeBiome.Data, maxMerge);
            }

            var center = new Vector2(
                origin.X + (gridWidth - 1) * TileSize * 0.5f,
                origin.Y + (gridHeight - 1) * TileSize * 0.5f);
            var (_, secondary, blend) = ResolveBiomeBlend(center);
            return (secondary, blend);
        }

        private void RecomputeSplatmapForChunk((int cx, int cz) key, ref TerrainChunk chunk)
        {
            int w = chunk.Heights.GetLength(0);
            int h = chunk.Heights.GetLength(1);
            chunk.Splatmap ??= new Vector4[w, h];

            string primaryId = _primaryBiomeByChunk.TryGetValue(key, out var biome) ? biome.Id : string.Empty;
            if (_biomeProvider is SimpleBiomeProvider simplePair)
            {
                var (pairPrimaryId, pairMergeId, _) = BiomeSampling.ResolveChunkBiomePair(
                    simplePair, _terrainGen, chunk.Origin, w, h, TileSize, 4f);
                if (!string.IsNullOrEmpty(pairPrimaryId))
                    primaryId = pairPrimaryId;
            }

            var (mergeBiome, maxMerge) = ResolveChunkRenderMerge(chunk.Origin, w, h);
            string mergeId = mergeBiome?.Id ?? string.Empty;
            bool hasMerge = !string.IsNullOrEmpty(mergeId);

            float[,]? mergeMap = null;
            if (hasMerge && _biomeProvider is SimpleBiomeProvider simple && !string.IsNullOrEmpty(primaryId))
                (mergeMap, _) = BiomeSampling.BuildChunkPairBlendMap(
                    simple, _terrainGen, chunk.Origin, w, h, TileSize, primaryId, mergeId, 2);

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float depth = 0f;
                if (chunk.BaseHeights != null)
                    depth = MathF.Max(0f, chunk.BaseHeights[x, z] - chunk.Heights[x, z]);
                float slope = ComputeSlope(chunk.Heights, x, z, w, h);

                Vector4 primary = ComputeSplatWeights(depth, key, x, z, primaryId, slope);

                if (hasMerge && mergeMap != null)
                {
                    float t = mergeMap[x, z];
                    if (t > 0.001f)
                    {
                        Vector4 merged = ComputeSplatWeights(depth, key, x, z, mergeId, slope);
                        chunk.Splatmap[x, z] = Vector4.Lerp(primary, merged, t);
                        continue;
                    }
                }
                chunk.Splatmap[x, z] = primary;
            }
        }

        private void RecomputeSplatmapRegion((int cx, int cz) key, ref TerrainChunk chunk, int x0, int z0, int x1, int z1)
        {
            int w = chunk.Heights.GetLength(0);
            int h = chunk.Heights.GetLength(1);
            chunk.Splatmap ??= new Vector4[w, h];

            string primaryId = _primaryBiomeByChunk.TryGetValue(key, out var biome) ? biome.Id : string.Empty;
            _biomeBlendByChunk.TryGetValue(key, out var blendInfo);
            string secondaryId = blendInfo.secondary?.Id ?? string.Empty;
            bool hasSecondary = !string.IsNullOrEmpty(secondaryId) && blendInfo.blend > 0.001f;

            int clampX1 = Math.Min(x1, w - 1);
            int clampZ1 = Math.Min(z1, h - 1);
            for (int z = Math.Max(0, z0); z <= clampZ1; z++)
            for (int x = Math.Max(0, x0); x <= clampX1; x++)
            {
                float depth = 0f;
                if (chunk.BaseHeights != null)
                    depth = MathF.Max(0f, chunk.BaseHeights[x, z] - chunk.Heights[x, z]);
                float slope = ComputeSlope(chunk.Heights, x, z, w, h);

                Vector4 primary = ComputeSplatWeights(depth, key, x, z, primaryId, slope);

                if (hasSecondary)
                {
                    // Use chunk-level blend for region updates (cheaper than per-vertex)
                    float t = blendInfo.blend;
                    if (t > 0.001f)
                    {
                        Vector4 secondary = ComputeSplatWeights(depth, key, x, z, secondaryId, slope);
                        chunk.Splatmap[x, z] = Vector4.Lerp(primary, secondary, t);
                        continue;
                    }
                }
                chunk.Splatmap[x, z] = primary;
            }
        }

        public void Render(CameraComponent camera)
        {
            if (!RenderBaseHeightmap) return;

            lock (_lock)
            {
                foreach (var kvp in _loadedChunks)
                {
                    var key = kvp.Key;
                    var chunk = kvp.Value;
                    if (!_primaryBiomeByChunk.TryGetValue(key, out var primaryBiome))
                    {
                        primaryBiome = ResolveChunkPrimaryBiome(chunk);
                        _primaryBiomeByChunk[key] = primaryBiome;
                    }
                    int gridW = chunk.Heights.GetLength(0);
                    int gridH = chunk.Heights.GetLength(1);
                    if (!_renderer.IsChunkVisibleForRender(chunk.Origin, TileSize, gridW, gridH, camera))
                        continue;

                    if (!_renderMergeByChunk.TryGetValue(key, out var mergeInfo))
                    {
                        mergeInfo = ResolveChunkRenderMerge(chunk.Origin, gridW, gridH);
                        _renderMergeByChunk[key] = mergeInfo;
                    }

                    var (mergeBiome, maxMerge) = mergeInfo;
                    _renderer.ApplyBiomeBlendTextures(primaryBiome, mergeBiome, maxMerge);
                    _renderer.RenderAt(chunk.Heights, TileSize, chunk.Origin, camera, chunk.BaseHeights, GetTerrainLayersForBiomeId(primaryBiome.Id), chunk.Splatmap);
                }
            }
        }

        public void RenderDebugChunkBounds(CameraComponent camera)
        {
            // Debug chunk bounds are drawn by VeilborneEngine as projected 2D overlays.
        }

        public IEnumerable<(Vector3 center, Vector3 size)> EnumerateChunkBounds()
        {
            lock (_lock)
            {
                foreach (var chunk in _loadedChunks.Values)
                {
                    float worldSize = ChunkSize * TileSize;
                    yield return (
                        new Vector3(chunk.Origin.X + worldSize * 0.5f, 0f, chunk.Origin.Y + worldSize * 0.5f),
                        new Vector3(worldSize, 2f, worldSize));
                }
            }
        }

        private static float EvalFalloff(float t, VoxelFalloff falloff)
        {
            t = Math.Clamp(t, 0f, 1f);
            return falloff switch
            {
                VoxelFalloff.Linear => 1f - t,
                VoxelFalloff.Exponential => (1f - t) * (1f - t),
                VoxelFalloff.Cosine => 0.5f * (1f + MathF.Cos(MathF.PI * t)),
                VoxelFalloff.Stepped => 1f, // Uniform strength within radius — blocky mining
                _ => 1f - t
            };
        }

        public Task DigSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            lock (_lock)
            {
                EnqueueTerrainEdit(new TerrainEditCommand(TerrainEditKind.Dig, worldCenter, radius, strength, falloff));
            }
            return Task.CompletedTask;
        }

        public bool TryMineAt(Vector3 position, float power, out ResourceBlockType blockType)
        {
            blockType = ResourceBlockType.None;
            lock (_lock)
            {
                var key = WorldToChunkKey(position.X, position.Z);
                if (!_loadedChunks.TryGetValue(key, out var chunk))
                    return false;
                if (!TryGetLocalIndex(key, position.X, position.Z, out int ix, out int iz))
                    return false;
                if (chunk.BaseHeights == null)
                    return false;

                float depth = MathF.Max(0f, chunk.BaseHeights[ix, iz] - chunk.Heights[ix, iz]);
                string biomeId = _primaryBiomeByChunk.TryGetValue(key, out var b)
                    ? b.Id
                    : _biomeProvider.GetBiomeAt(new Vector2(position.X, position.Z), _terrainGen).Id;
                var lc = GetTerrainLayersForBiomeId(biomeId);
                var voxelKey = (ix, iz);
                if (!chunk.ResourceVoxels.TryGetValue(voxelKey, out var voxel))
                {
                    voxel = CreateResourceVoxelForCell(key, ix, iz, depth);
                    chunk.ResourceVoxels[voxelKey] = voxel;
                }

                if (power <= 0f)
                {
                    blockType = voxel.Type;
                    return voxel.Type != ResourceBlockType.None;
                }

                voxel.Density = Math.Clamp(voxel.Density - power, 0f, 1f);
                if (voxel.Density <= 0f)
                {
                    blockType = voxel.Type;
                    chunk.ResourceVoxels.Remove(voxelKey);
                    if (voxel.Type != ResourceBlockType.None && depth >= lc.SubsurfaceDepth)
                    {
                        float carve = MathF.Max(0.02f, power * 0.25f);
                        float floor = chunk.BaseHeights[ix, iz] - MathF.Max(0.2f, _config.Config.Dig.MaxDepth);
                        chunk.Heights[ix, iz] = MathF.Max(floor, chunk.Heights[ix, iz] - carve);
                        chunk.MarkDirtyCell(ix, iz);
                        chunk.Dirty = true;
                        chunk.Version++;
                    }
                    int pd = 1;
                    RecomputeSplatmapRegion(key, ref chunk,
                        Math.Clamp(ix - pd, 0, ChunkSize),
                        Math.Clamp(iz - pd, 0, ChunkSize),
                        Math.Clamp(ix + pd, 0, ChunkSize),
                        Math.Clamp(iz + pd, 0, ChunkSize));
                    _loadedChunks[key] = chunk;
                    return true;
                }

                chunk.ResourceVoxels[voxelKey] = voxel;
                _loadedChunks[key] = chunk;
                return false;
            }
        }

        private void InitializeChunkLayersAndResources((int cx, int cz) key, ref TerrainChunk chunk, string biomeId)
        {
            int w = chunk.Heights.GetLength(0);
            int h = chunk.Heights.GetLength(1);
            chunk.Splatmap = new Vector4[w, h];
            chunk.ResourceVoxels.Clear();

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float depth = 0f;
                if (chunk.BaseHeights != null)
                    depth = MathF.Max(0f, chunk.BaseHeights[x, z] - chunk.Heights[x, z]);
                float slope = ComputeSlope(chunk.Heights, x, z, w, h);
                chunk.Splatmap[x, z] = ComputeSplatWeights(depth, key, x, z, biomeId, slope);
            }
        }

        private static float ComputeSlope(float[,] heights, int x, int z, int w, int h)
        {
            float center = heights[x, z];
            float left  = x > 0 ? heights[x - 1, z] : center;
            float right = x < w - 1 ? heights[x + 1, z] : center;
            float up    = z > 0 ? heights[x, z - 1] : center;
            float down  = z < h - 1 ? heights[x, z + 1] : center;
            float dx = (right - left) * 0.5f;
            float dz = (down - up) * 0.5f;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private Vector4 ComputeSplatWeights(float depth, (int cx, int cz) key, int ix, int iz, string biomeId, float slope)
        {
            var lc = GetTerrainLayersForBiomeId(biomeId);

            // World-space smooth noise avoids blocky per-vertex grass/mud seams.
            float depthVar = lc.DepthVariation;
            float worldX = (key.cx * ChunkSize + ix) * TileSize;
            float worldZ = (key.cz * ChunkSize + iz) * TileSize;
            float cellNoise = SampleSmoothDepthNoise(worldX, worldZ) * depthVar;
            float effSubDepth = MathF.Max(0.05f, lc.SubsurfaceDepth + cellNoise);
            float effDeepDepth = MathF.Max(effSubDepth + 0.1f, lc.DeepDepth + cellNoise * 1.5f);

            float grass = Math.Clamp(1f - depth / effSubDepth, 0f, 1f);
            grass = grass * grass * (3f - 2f * grass);
            float mud = 0f;
            float rock = 0f;
            float mineral = 0f;

            if (depth > 0f)
            {
                float subT = Math.Clamp(depth / effSubDepth, 0f, 1f);
                float deepT = Math.Clamp((depth - effSubDepth) / MathF.Max(0.05f, effDeepDepth - effSubDepth), 0f, 1f);
                mud = Math.Clamp(subT * (1f - deepT), 0f, 1f);
                rock = deepT;
                mineral = ComputeMineralWeight(key, ix, iz, depth, biomeId);
                rock = Math.Clamp(rock * (1f - mineral), 0f, 1f);
            }

            // Slope-based rock blending: steep areas show rock regardless of depth
            float slopeThreshold = lc.SlopeRockThreshold;
            float slopeRange = MathF.Max(0.01f, lc.SlopeBlendRange);
            float slopeBlend = Math.Clamp((slope - slopeThreshold) / slopeRange, 0f, 1f);
            if (slopeBlend > 0f)
            {
                // Smoothstep for natural look
                slopeBlend = slopeBlend * slopeBlend * (3f - 2f * slopeBlend);
                grass *= (1f - slopeBlend);
                mud *= (1f - slopeBlend * 0.7f);
                rock = MathF.Max(rock, slopeBlend);
            }

            float sum = grass + mud + rock + mineral;
            if (sum <= 1e-5f) return new Vector4(1f, 0f, 0f, 0f);
            return new Vector4(grass / sum, mud / sum, rock / sum, mineral / sum);
        }

        private static float SampleSmoothDepthNoise(float worldX, float worldZ)
        {
            const float frequency = 0.14f;
            float x = worldX * frequency;
            float z = worldZ * frequency;
            int ix = (int)MathF.Floor(x);
            int iz = (int)MathF.Floor(z);
            float fx = x - ix;
            float fz = z - iz;
            float sx = fx * fx * (3f - 2f * fx);
            float sz = fz * fz * (3f - 2f * fz);
            float n00 = DepthNoiseUnit(ix, iz);
            float n10 = DepthNoiseUnit(ix + 1, iz);
            float n01 = DepthNoiseUnit(ix, iz + 1);
            float n11 = DepthNoiseUnit(ix + 1, iz + 1);
            float nx0 = n00 + (n10 - n00) * sx;
            float nx1 = n01 + (n11 - n01) * sx;
            return (nx0 + (nx1 - nx0) * sz) * 2f - 1f;
        }

        private static float DepthNoiseUnit(int ix, int iz)
        {
            unchecked
            {
                int h = ix * 374761393 ^ iz * 668265263;
                h ^= h >> 13;
                h *= 1274126177;
                return (h & 0x7FFFFFFF) / (float)int.MaxValue;
            }
        }

        private float ComputeMineralWeight((int cx, int cz) key, int ix, int iz, float depth, string biomeId)
        {
            var rules = GetMiningRulesForBiomeId(biomeId);
            float best = 0f;
            foreach (var rule in rules)
            {
                if (depth < rule.OreMinDepth || depth > rule.OreMaxDepth)
                    continue;
                int h = HashCode.Combine(_config.Seed, key.cx, key.cz, ix, iz, biomeId.GetHashCode(StringComparison.OrdinalIgnoreCase), rule.OreType.GetHashCode(StringComparison.OrdinalIgnoreCase));
                float n = HashTo01(h * 1109 + (int)(rule.OreNoiseFrequency * 100f));
                if (n < rule.OreThreshold)
                    continue;
                float w = Math.Clamp((n - rule.OreThreshold) / MathF.Max(0.01f, 1f - rule.OreThreshold), 0f, 1f) * Math.Clamp(rule.OreSpawnChance * 3f, 0f, 1f);
                if (w > best) best = w;
            }
            return best;
        }

        private ResourceVoxel CreateResourceVoxelForCell((int cx, int cz) key, int ix, int iz, float depth)
        {
            string biomeId;
            if (_primaryBiomeByChunk.TryGetValue(key, out var biome))
            {
                biomeId = biome.Id;
            }
            else if (_loadedChunks.TryGetValue(key, out var chunk))
            {
                float wx = chunk.Origin.X + ix * TileSize;
                float wz = chunk.Origin.Y + iz * TileSize;
                biomeId = _biomeProvider.GetBiomeAt(new Vector2(wx, wz), _terrainGen).Id;
            }
            else
            {
                biomeId = string.Empty;
            }
            var lc = GetTerrainLayersForBiomeId(biomeId);
            ResourceBlockType type = ResourceBlockType.Grass;
            if (depth >= lc.DeepDepth) type = ResourceBlockType.Rock;
            else if (depth >= lc.SubsurfaceDepth) type = ResourceBlockType.Dirt;

            var rules = GetMiningRulesForBiomeId(biomeId);
            ResourceBlockType bestType = ResourceBlockType.None;
            float bestScore = float.NegativeInfinity;
            foreach (var rule in rules)
            {
                if (depth < rule.OreMinDepth || depth > rule.OreMaxDepth)
                    continue;
                float n = HashTo01(HashCode.Combine(_config.Seed, key.cx, key.cz, ix, iz, rule.OreType.GetHashCode(StringComparison.OrdinalIgnoreCase), 79));
                if (n < rule.OreThreshold)
                    continue;
                float score = (n - rule.OreThreshold) * Math.Clamp(rule.OreSpawnChance, 0f, 1f);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestType = ParseOreType(rule.OreType);
                }
            }
            if (bestType != ResourceBlockType.None)
                type = bestType;

            return new ResourceVoxel
            {
                LocalPosition = new Vector3(ix * TileSize, 0f, iz * TileSize),
                Type = type,
                Density = 1f,
                BiomeId = biomeId
            };
        }

        private static float HashTo01(int seed)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                return (x % 10000) / 10000f;
            }
        }

        private static ResourceBlockType ParseOreType(string oreType)
        {
            if (string.IsNullOrWhiteSpace(oreType)) return ResourceBlockType.Rock;
            return oreType.Trim().ToLowerInvariant() switch
            {
                "coal" => ResourceBlockType.Coal,
                "iron" => ResourceBlockType.Iron,
                "copper" => ResourceBlockType.Copper,
                _ => ResourceBlockType.Rock
            };
        }

        private void RegisterBiomeConfig(BiomeData data)
        {
            if (string.IsNullOrWhiteSpace(data.Id))
                return;
            _biomeConfigById[data.Id] = data;
        }

        private BiomeData? GetBiomeConfigForId(string biomeId)
        {
            if (string.IsNullOrWhiteSpace(biomeId))
                return null;
            if (_biomeConfigById.TryGetValue(biomeId, out var cached))
                return cached;
            foreach (var biome in _primaryBiomeByChunk.Values)
            {
                if (!string.Equals(biome.Id, biomeId, StringComparison.OrdinalIgnoreCase))
                    continue;
                _biomeConfigById[biomeId] = biome;
                return biome;
            }
            return null;
        }

        private TerrainLayerConfig GetTerrainLayersForBiomeId(string biomeId)
        {
            var data = GetBiomeConfigForId(biomeId);
            if (data is null)
                throw new InvalidOperationException($"Missing biome config for '{biomeId}' while resolving terrain layers.");
            return data.TerrainLayers;
        }

        private IReadOnlyList<BiomeOreRule> GetMiningRulesForBiomeId(string biomeId)
        {
            var data = GetBiomeConfigForId(biomeId);
            if (data?.Mining?.Ores is { Count: > 0 } ores)
                return ores;
            return Array.Empty<BiomeOreRule>();
        }

        private static void SmoothPatch(TerrainChunk chunk, int x0, int z0, int x1, int z1, float maxDepth, float smoothness)
        {
            int sx0 = Math.Max(1, x0 - 1);
            int sz0 = Math.Max(1, z0 - 1);
            int sx1 = Math.Min(chunk.Heights.GetLength(0) - 2, x1 + 1);
            int sz1 = Math.Min(chunk.Heights.GetLength(1) - 2, z1 + 1);
            if (sx0 > sx1 || sz0 > sz1) return;

            int w = sx1 - sx0 + 1;
            int h = sz1 - sz0 + 1;
            var temp = new float[w, h];

            for (int z = sz0; z <= sz1; z++)
            for (int x = sx0; x <= sx1; x++)
            {
                float sum = 0f;
                int cnt = 0;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int nz = z + dz;
                    sum += chunk.Heights[nx, nz];
                    cnt++;
                }

                float avg = cnt > 0 ? sum / cnt : chunk.Heights[x, z];
                float blended = chunk.Heights[x, z] + (avg - chunk.Heights[x, z]) * smoothness;
                float floor = chunk.BaseHeights != null ? chunk.BaseHeights[x, z] - maxDepth : float.NegativeInfinity;
                temp[x - sx0, z - sz0] = MathF.Max(floor, blended);
            }

            for (int z = sz0; z <= sz1; z++)
            for (int x = sx0; x <= sx1; x++)
                chunk.Heights[x, z] = temp[x - sx0, z - sz0];
        }

        public Task PlaceSphereAsync(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            lock (_lock)
            {
                EnqueueTerrainEdit(new TerrainEditCommand(TerrainEditKind.Place, worldCenter, radius, strength, falloff));
            }
            return Task.CompletedTask;
        }

        public int GetMaxVersionForBounds(float minX, float minZ, float maxX, float maxZ)
        {
            lock (_lock)
            {
                int minCx = (int)MathF.Floor(minX / (ChunkSize * TileSize));
                int maxCx = (int)MathF.Floor(maxX / (ChunkSize * TileSize));
                int minCz = (int)MathF.Floor(minZ / (ChunkSize * TileSize));
                int maxCz = (int)MathF.Floor(maxZ / (ChunkSize * TileSize));
                int maxVer = 0;
                for (int cz = minCz; cz <= maxCz; cz++)
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    if (_loadedChunks.TryGetValue((cx, cz), out var ch))
                        maxVer = Math.Max(maxVer, ch.Version);
                }
                return maxVer;
            }
        }

        public Task PumpAsyncJobs(bool warmupMode = false)
        {
            _isWarmupMode = warmupMode;
            lock (_lock)
            {
                int installBudget = warmupMode ? int.MaxValue : MaxEditableChunkInstallsPerFrame;
                InstallCompletedChunks(installBudget);
                ProcessPendingTerrainEdits(Math.Max(1, _config.Config.Dig.MaxTerrainEditsPerFrame));
                int spawnBudget = warmupMode ? int.MaxValue : MaxObjectSpawnsPerFrame;
                ProcessPendingObjectSpawns(spawnBudget);
            }
            return Task.CompletedTask;
        }

        private void EnqueueTerrainEdit(TerrainEditCommand command)
        {
            if (_pendingTerrainEdits.Count >= MaxTerrainEditsQueued)
                _pendingTerrainEdits.Dequeue();
            _pendingTerrainEdits.Enqueue(command);
        }

        private void ProcessPendingTerrainEdits(int budget)
        {
            int remaining = Math.Max(0, budget);
            while (remaining > 0 && _pendingTerrainEdits.Count > 0)
            {
                var cmd = _pendingTerrainEdits.Dequeue();
                if (cmd.Kind == TerrainEditKind.Dig)
                    ApplyDigSphere(cmd.Center, cmd.Radius, cmd.Strength, cmd.Falloff);
                else
                    ApplyPlaceSphere(cmd.Center, cmd.Radius, cmd.Strength, cmd.Falloff);
                remaining--;
            }
        }

        private void ApplyDigSphere(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            float maxDepth = MathF.Max(0.2f, _config.Config.Dig.MaxDepth);
            float smoothness = Math.Clamp(_config.Config.Dig.Smoothness, 0f, 0.6f);
            float blockStep = MathF.Max(0f, _config.Config.Dig.BlockStepSize);
            float minX = worldCenter.X - radius;
            float maxX = worldCenter.X + radius;
            float minZ = worldCenter.Z - radius;
            float maxZ = worldCenter.Z + radius;

            int minCx = (int)MathF.Floor(minX / (ChunkSize * TileSize));
            int maxCx = (int)MathF.Floor(maxX / (ChunkSize * TileSize));
            int minCz = (int)MathF.Floor(minZ / (ChunkSize * TileSize));
            int maxCz = (int)MathF.Floor(maxZ / (ChunkSize * TileSize));

            for (int cz = minCz; cz <= maxCz; cz++)
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                var key = (cx, cz);
                if (!_loadedChunks.TryGetValue(key, out var chunk)) continue;

                float originX = chunk.Origin.X;
                float originZ = chunk.Origin.Y;

                int x0 = Math.Clamp((int)MathF.Floor((minX - originX) / TileSize), 0, ChunkSize);
                int x1 = Math.Clamp((int)MathF.Ceiling((maxX - originX) / TileSize), 0, ChunkSize);
                int z0 = Math.Clamp((int)MathF.Floor((minZ - originZ) / TileSize), 0, ChunkSize);
                int z1 = Math.Clamp((int)MathF.Ceiling((maxZ - originZ) / TileSize), 0, ChunkSize);

                for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float wx = originX + ix * TileSize;
                    float wz = originZ + iz * TileSize;
                    float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(worldCenter.X, worldCenter.Z));
                    if (d > radius) continue;
                    float t = d / radius;
                    float delta = strength * EvalFalloff(t, falloff) * 0.16f;
                    float floor = chunk.BaseHeights != null ? chunk.BaseHeights[ix, iz] - maxDepth : float.NegativeInfinity;
                    float next = chunk.Heights[ix, iz] - delta;
                    next = MathF.Max(floor, next);

                    // Quantize to block steps for discrete mining feel
                    if (blockStep > 0.001f)
                        next = MathF.Floor(next / blockStep) * blockStep;

                    chunk.Heights[ix, iz] = next;
                }

                // Skip smoothing for stepped (blocky) falloff — preserves sharp edges
                if (smoothness > 0.001f && falloff != VoxelFalloff.Exponential && falloff != VoxelFalloff.Stepped)
                    SmoothPatch(chunk, x0, z0, x1, z1, maxDepth, smoothness);

                // Only recompute splatmap in the affected region, not the whole chunk
                int pd = 1;
                int sx0 = Math.Clamp(x0 - pd, 0, ChunkSize);
                int sz0 = Math.Clamp(z0 - pd, 0, ChunkSize);
                int sx1 = Math.Clamp(x1 + pd, 0, ChunkSize);
                int sz1 = Math.Clamp(z1 + pd, 0, ChunkSize);
                RecomputeSplatmapRegion(key, ref chunk, sx0, sz0, sx1, sz1);

                chunk.MarkDirtyRect(sx0, sz0, sx1, sz1);

                chunk.IsMeshGenerated = false;
                chunk.Dirty = true;
                chunk.Version++;
                _loadedChunks[key] = chunk;
            }
        }

        private void ApplyPlaceSphere(Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            float minX = worldCenter.X - radius;
            float maxX = worldCenter.X + radius;
            float minZ = worldCenter.Z - radius;
            float maxZ = worldCenter.Z + radius;

            int minCx = (int)MathF.Floor(minX / (ChunkSize * TileSize));
            int maxCx = (int)MathF.Floor(maxX / (ChunkSize * TileSize));
            int minCz = (int)MathF.Floor(minZ / (ChunkSize * TileSize));
            int maxCz = (int)MathF.Floor(maxZ / (ChunkSize * TileSize));

            for (int cz = minCz; cz <= maxCz; cz++)
            for (int cx = minCx; cx <= maxCx; cx++)
            {
                var key = (cx, cz);
                if (!_loadedChunks.TryGetValue(key, out var chunk)) continue;

                float originX = chunk.Origin.X;
                float originZ = chunk.Origin.Y;

                int x0 = Math.Clamp((int)MathF.Floor((minX - originX) / TileSize), 0, ChunkSize);
                int x1 = Math.Clamp((int)MathF.Ceiling((maxX - originX) / TileSize), 0, ChunkSize);
                int z0 = Math.Clamp((int)MathF.Floor((minZ - originZ) / TileSize), 0, ChunkSize);
                int z1 = Math.Clamp((int)MathF.Ceiling((maxZ - originZ) / TileSize), 0, ChunkSize);

                for (int iz = z0; iz <= z1; iz++)
                for (int ix = x0; ix <= x1; ix++)
                {
                    float wx = originX + ix * TileSize;
                    float wz = originZ + iz * TileSize;
                    float d = Vector2.Distance(new Vector2(wx, wz), new Vector2(worldCenter.X, worldCenter.Z));
                    if (d > radius) continue;
                    float t = d / radius;
                    float delta = strength * EvalFalloff(t, falloff);
                    chunk.Heights[ix, iz] += delta;
                }

                int pd = 1;
                int sx0 = Math.Clamp(x0 - pd, 0, ChunkSize);
                int sz0 = Math.Clamp(z0 - pd, 0, ChunkSize);
                int sx1 = Math.Clamp(x1 + pd, 0, ChunkSize);
                int sz1 = Math.Clamp(z1 + pd, 0, ChunkSize);
                RecomputeSplatmapRegion(key, ref chunk, sx0, sz0, sx1, sz1);

                chunk.MarkDirtyRect(sx0, sz0, sx1, sz1);

                chunk.IsMeshGenerated = false;
                chunk.Dirty = true;
                chunk.Version++;
                _loadedChunks[key] = chunk;
            }
        }

        private void ProcessPendingObjectSpawns(int budget)
        {
            int remaining = Math.Max(0, budget);
            while (remaining > 0 && _pendingSpawnOrder.Count > 0)
            {
                var key = _pendingSpawnOrder.Dequeue();
                if (!_pendingSpawnsByChunk.TryGetValue(key, out var batch) || batch.Objects is not { } objects)
                {
                    _pendingSpawnsByChunk.Remove(key);
                    continue;
                }
                if (!_entitiesByChunk.TryGetValue(key, out var entities))
                {
                    _pendingSpawnsByChunk.Remove(key);
                    continue;
                }

                int spawnCount = Math.Min(6, remaining);
                while (spawnCount-- > 0 && batch.Cursor < objects.Count)
                {
                    var obj = objects[batch.Cursor++];
                    entities.Add(CreateWorldObjectEntity(obj, key, 0, batch.BiomeId));
                    remaining--;
                }

                if (batch.Cursor < objects.Count)
                    _pendingSpawnOrder.Enqueue(key);
                else
                    _pendingSpawnsByChunk.Remove(key);
            }
        }

        private Entity CreateWorldObjectEntity(SpawnedObject obj, (int cx, int cz) key, int lodLevel, string biomeId)
        {
            var entity = _entityRegistry.CreateEntity();
            float groundedY = SampleHeight(obj.Position.X, obj.Position.Z);
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
    }
}
