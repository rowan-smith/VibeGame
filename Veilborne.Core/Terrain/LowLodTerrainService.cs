using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;
using Vector4 = System.Numerics.Vector4;

namespace Veilborne.Terrain
{
    public class LowLodTerrainService
    {
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

        private static KeyValuePair<(int cx, int cz), TerrainChunk>[] SnapshotPairsSafe(Dictionary<(int cx, int cz), TerrainChunk> chunks)
        {
            while (true)
            {
                try
                {
                    return chunks.ToArray();
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

        private static TerrainChunk[] SnapshotChunksSafe(Dictionary<(int cx, int cz), TerrainChunk> chunks)
        {
            while (true)
            {
                try
                {
                    return chunks.Values.ToArray();
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

        private static Dictionary<(int cx, int cz), BiomeData> SnapshotBiomesSafe(Dictionary<(int cx, int cz), BiomeData> biomesByChunk)
        {
            while (true)
            {
                try
                {
                    return biomesByChunk.ToDictionary(static kv => kv.Key, static kv => kv.Value);
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

        public float TileSize { get; } = 4.0f;
        public int ChunkSize { get; } = 128;
        public int InnerExclusionRadiusChunks { get; set; }
        public int MaxConcurrentJobs { get; set; } = 8;

        private readonly Dictionary<(int cx, int cz), TerrainChunk> _loadedChunks = new();
        private readonly Dictionary<(int cx, int cz), BiomeData> _biomeByChunk = new();
        private readonly Dictionary<(int cx, int cz), (BiomeData? secondary, float blend)> _biomeBlendByChunk = new();

        private readonly IBiomeProvider _biomeProvider;
        private readonly ITerrainRenderer _renderer;
        private readonly EditableTerrainService _editable;
        private readonly ITerrainGenerator _terrainGen;
        private readonly IWorldConfigService _config;

        // Async generation state
        private readonly HashSet<(int cx, int cz)> _generating = new();
        private readonly ConcurrentQueue<((int cx, int cz) key, float[,] heights, Vector2 origin, BiomeData biome)> _completed = new();
        private readonly HashSet<(int cx, int cz)> _desiredKeysScratch = new();
        private readonly List<(int cx, int cz)> _toRemoveScratch = new();
        private int _lastDesiredChunkCount;

        public LowLodTerrainService(EditableTerrainService editable, IBiomeProvider biomeProvider, ITerrainRenderer renderer, ITerrainGenerator terrainGen, IWorldConfigService config)
        {
            _editable = editable;
            _biomeProvider = biomeProvider;
            _renderer = renderer;
            _terrainGen = terrainGen;
            _config = config;
        }

        public Dictionary<(int cx, int cz), TerrainChunk> GetLoadedChunks() => _loadedChunks;
        public int DesiredChunkCount => _lastDesiredChunkCount;
        public int LoadedChunkCount => _loadedChunks.Count;
        public int GeneratingChunkCount => _generating.Count;

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            int centerX = (int)MathF.Floor(worldPos.X / (ChunkSize * TileSize));
            int centerZ = (int)MathF.Floor(worldPos.Z / (ChunkSize * TileSize));

            _desiredKeysScratch.Clear();
            for (int z = -radiusChunks; z <= radiusChunks; z++)
            for (int x = -radiusChunks; x <= radiusChunks; x++)
            {
                int ring = Math.Max(Math.Abs(x), Math.Abs(z));
                if (ring < InnerExclusionRadiusChunks) continue;
                var key = (centerX + x, centerZ + z);
                _desiredKeysScratch.Add(key);
                if (!_loadedChunks.ContainsKey(key) && !_generating.Contains(key))
                {
                    if (_generating.Count >= System.Math.Max(1, MaxConcurrentJobs))
                        continue;
                    float originX = key.Item1 * ChunkSize * TileSize;
                    float originZ = key.Item2 * ChunkSize * TileSize;
                    var origin = new Vector2(originX, originZ);

                    _generating.Add(key);
                    _ = Task.Run(() =>
                    {
                        // Coarse sampling in background
                        float[,] heights = new float[ChunkSize + 1, ChunkSize + 1];
                        for (int zz = 0; zz <= ChunkSize; zz++)
                        for (int xx = 0; xx <= ChunkSize; xx++)
                        {
                            float wx = origin.X + xx * TileSize;
                            float wz = origin.Y + zz * TileSize;
                            heights[xx, zz] = _editable.SampleHeight(wx, wz);
                        }
                        var center = new Vector2(
                            origin.X + ChunkSize * TileSize * 0.5f,
                            origin.Y + ChunkSize * TileSize * 0.5f);
                        var (biome, secondaryBiome, secondaryBlend) = ResolveBiomeBlend(center);
                        lock (_biomeBlendByChunk)
                            _biomeBlendByChunk[key] = (secondaryBiome?.Data, secondaryBlend);
                        _completed.Enqueue((key, heights, origin, biome.Data));
                    });
                }
            }

            // unload chunks outside desired set
            _toRemoveScratch.Clear();
            var loadedKeysSnapshot = SnapshotKeysSafe(_loadedChunks);
            foreach (var key in loadedKeysSnapshot)
            {
                if (_desiredKeysScratch.Contains(key))
                    continue;

                // Keep a small hysteresis ring so LOD coverage stays continuous during streaming churn.
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
                lock (_biomeBlendByChunk)
                    _biomeBlendByChunk.Remove(key);
            }

            _lastDesiredChunkCount = _desiredKeysScratch.Count;
        }

        public Task PumpAsyncJobs(int maxInstallsPerFrame = int.MaxValue, bool warmupMode = false)
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
                _loadedChunks[item.key] = new TerrainChunk
                {
                    Heights = item.heights,
                    BaseHeights = (float[,])item.heights.Clone(),
                    Splatmap = BuildSplatmap(item.heights, item.heights, item.biome, secBiome, secBlend, item.origin),
                    Origin = item.origin,
                    IsMeshGenerated = false,
                    BuiltFromVersion = -1
                };
                _biomeByChunk[item.key] = item.biome;
                installs++;
                if (!warmupMode && installs >= maxInstallsPerFrame)
                    break;
            }
            return Task.CompletedTask;
        }

        public void Render(CameraComponent camera, HashSet<(int cx, int cz)>? exclude = null)
        {
            var loadedPairs = SnapshotPairsSafe(_loadedChunks);
            var biomeByChunkSnapshot = SnapshotBiomesSafe(_biomeByChunk);
            foreach (var kvp in loadedPairs)
            {
                var key = kvp.Key;
                if (exclude != null && exclude.Contains(key))
                    continue;
                var chunk = kvp.Value;
                BiomeData primaryBiome;
                if (biomeByChunkSnapshot.TryGetValue(key, out var cachedPrimary))
                    primaryBiome = cachedPrimary;
                else
                    primaryBiome = ResolveBiomeBlend(new Vector2(
                        chunk.Origin.X + ChunkSize * TileSize * 0.5f,
                        chunk.Origin.Y + ChunkSize * TileSize * 0.5f)).primary.Data;

                int gridW = chunk.Heights.GetLength(0);
                int gridH = chunk.Heights.GetLength(1);
                var (mergeBiome, maxMerge) = ResolveChunkRenderMerge(chunk.Origin, gridW, gridH);
                _renderer.ApplyBiomeBlendTextures(primaryBiome, mergeBiome, maxMerge);
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

        public void RenderDebugChunkBounds(CameraComponent camera)
        {
            // Debug chunk bounds are drawn by VeilborneEngine as projected 2D overlays.
        }

        private (IBiome primary, IBiome? secondary, float blend) ResolveBiomeBlend(Vector2 centerWorld)
        {
            if (_biomeProvider is SimpleBiomeProvider simple)
                return simple.GetBiomeBlendAt(centerWorld, _terrainGen);

            var primary = _biomeProvider.GetBiomeAt(centerWorld, _terrainGen);
            return (primary, null, 0f);
        }

        private (BiomeData? mergeBiome, float maxMerge) ResolveChunkRenderMerge(Vector2 origin, int gridWidth, int gridHeight)
        {
            if (_biomeProvider is SimpleBiomeProvider simple)
            {
                var (_, _, _, _, maxMerge, mergeBiome) = BiomeSampling.ResolveCornerCrossfade(
                    simple, _terrainGen, origin, gridWidth, gridHeight, TileSize);
                if (mergeBiome is not null && maxMerge > 0.001f)
                    return (mergeBiome.Data, maxMerge);
            }

            var center = new Vector2(
                origin.X + (gridWidth - 1) * TileSize * 0.5f,
                origin.Y + (gridHeight - 1) * TileSize * 0.5f);
            var (_, secondary, blend) = ResolveBiomeBlend(center);
            return (secondary?.Data, blend);
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

        private Vector4[,] BuildSplatmap(float[,] heights, float[,]? baseHeights, BiomeData biome,
            BiomeData? secondaryBiome = null, float blendFactor = 0f, Vector2 origin = default)
        {
            int w = heights.GetLength(0);
            int h = heights.GetLength(1);
            var splat = new Vector4[w, h];

            BiomeData? effectiveMerge = secondaryBiome;
            float effectiveMaxMerge = blendFactor;
            float[,]? mergeMap = null;
            if (_biomeProvider is SimpleBiomeProvider simpleProvider)
            {
                var (_, _, _, _, maxMerge, mergeBiome) = BiomeSampling.ResolveCornerCrossfade(
                    simpleProvider, _terrainGen, origin, w, h, TileSize);
                if (mergeBiome is not null && maxMerge > 0.001f)
                {
                    effectiveMerge = mergeBiome.Data;
                    effectiveMaxMerge = maxMerge;
                }
                mergeMap = BiomeSampling.BuildVertexBlendMap(simpleProvider, _terrainGen, origin, w, h, TileSize);
            }

            bool hasMerge = effectiveMerge != null && effectiveMaxMerge > 0.001f && mergeMap != null;

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float depth = 0f;
                if (baseHeights != null)
                    depth = MathF.Max(0f, baseHeights[x, z] - heights[x, z]);

                float center = heights[x, z];
                float left  = x > 0 ? heights[x - 1, z] : center;
                float right = x < w - 1 ? heights[x + 1, z] : center;
                float up    = z > 0 ? heights[x, z - 1] : center;
                float down  = z < h - 1 ? heights[x, z + 1] : center;
                float dx = (right - left) * 0.5f;
                float dz = (down - up) * 0.5f;
                float slope = MathF.Sqrt(dx * dx + dz * dz);

                Vector4 primary = ComputeSplatForLayers(biome.TerrainLayers, depth, slope);

                if (hasMerge)
                {
                    float vertexBlend = mergeMap![x, z];
                    if (vertexBlend > 0.001f)
                    {
                        Vector4 merged = ComputeSplatForLayers(effectiveMerge!.TerrainLayers, depth, slope);
                        splat[x, z] = Vector4.Lerp(primary, merged, vertexBlend);
                        continue;
                    }
                }

                splat[x, z] = primary;
            }
            return splat;
        }

        private static Vector4 ComputeSplatForLayers(TerrainLayerConfig layers, float depth, float slope)
        {
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

            float slopeBlend = Math.Clamp((slope - layers.SlopeRockThreshold) / MathF.Max(0.01f, layers.SlopeBlendRange), 0f, 1f);
            slopeBlend = slopeBlend * slopeBlend * (3f - 2f * slopeBlend);
            if (slopeBlend > 0f)
            {
                top *= (1f - slopeBlend);
                dirt *= (1f - slopeBlend * 0.7f);
                rock = MathF.Max(rock, slopeBlend);
            }

            float sum = top + dirt + rock;
            return sum > 1e-5f
                ? new Vector4(top / sum, dirt / sum, rock / sum, 0f)
                : new Vector4(1f, 0f, 0f, 0f);
        }
    }
}
