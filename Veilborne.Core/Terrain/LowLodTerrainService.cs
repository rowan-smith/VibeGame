using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;
using Veilborne.Core;
using Veilborne.Core.Ecs;
using Veilborne.Interfaces;
using Veilborne.Biomes;
using Veilborne.Core.Ecs.Components;
using Vector4 = System.Numerics.Vector4;

namespace Veilborne.Terrain
{
    public class LowLodTerrainService
    {
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

            var desired = new HashSet<(int cx, int cz)>();
            for (int z = -radiusChunks; z <= radiusChunks; z++)
            for (int x = -radiusChunks; x <= radiusChunks; x++)
            {
                int ring = Math.Max(Math.Abs(x), Math.Abs(z));
                if (ring < InnerExclusionRadiusChunks) continue;
                var key = (centerX + x, centerZ + z);
                desired.Add(key);
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
            var toRemove = new List<(int cx, int cz)>();
            foreach (var key in _loadedChunks.Keys)
                if (!desired.Contains(key)) toRemove.Add(key);
            foreach (var key in toRemove)
            {
                _loadedChunks.Remove(key);
                _biomeByChunk.Remove(key);
                lock (_biomeBlendByChunk)
                    _biomeBlendByChunk.Remove(key);
            }

            _lastDesiredChunkCount = desired.Count;
        }

        public async Task PumpAsyncJobs(int maxInstallsPerFrame = int.MaxValue)
        {
            int installs = 0;
            while (_completed.TryDequeue(out var item))
            {
                _generating.Remove(item.key);
                _loadedChunks[item.key] = new TerrainChunk
                {
                    Heights = item.heights,
                    BaseHeights = (float[,])item.heights.Clone(),
                    Splatmap = BuildSplatmap(item.heights),
                    Origin = item.origin,
                    IsMeshGenerated = false,
                    BuiltFromVersion = -1
                };
                _biomeByChunk[item.key] = item.biome;
                installs++;
                if (installs >= maxInstallsPerFrame) break;
                await Task.Yield();
            }
        }

        public void Render(CameraComponent camera, HashSet<(int cx, int cz)>? exclude = null)
        {
            foreach (var kvp in _loadedChunks)
            {
                var key = kvp.Key;
                if (exclude != null && exclude.Contains(key))
                    continue;
                var chunk = kvp.Value;
                BiomeData primaryBiome;
                if (_biomeByChunk.TryGetValue(key, out var cachedPrimary))
                    primaryBiome = cachedPrimary;
                else
                    primaryBiome = ResolveBiomeBlend(new Vector2(
                        chunk.Origin.X + ChunkSize * TileSize * 0.5f,
                        chunk.Origin.Y + ChunkSize * TileSize * 0.5f)).primary.Data;

                (BiomeData? secondary, float blend) blendInfo;
                lock (_biomeBlendByChunk)
                    blendInfo = _biomeBlendByChunk.TryGetValue(key, out var info) ? info : (null, 0f);
                _renderer.ApplyBiomeBlendTextures(primaryBiome, blendInfo.secondary, blendInfo.blend);
                _renderer.RenderAt(chunk.Heights, TileSize, chunk.Origin, camera, chunk.BaseHeights, _config.Config.TerrainLayers, chunk.Splatmap);
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

        private Vector4[,] BuildSplatmap(float[,] heights)
        {
            int w = heights.GetLength(0);
            int h = heights.GetLength(1);
            var splat = new Vector4[w, h];
            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                float slope = EstimateSlope(heights, x, z);
                float top = Math.Clamp(1f - slope * 1.3f, 0f, 1f);
                float dirt = Math.Clamp(1f - top, 0f, 1f);
                float rock = Math.Clamp((slope - 0.45f) / 0.55f, 0f, 1f);
                float sum = top + dirt + rock;
                splat[x, z] = sum > 1e-5f
                    ? new Vector4(top / sum, dirt / sum, rock / sum, 0f)
                    : new Vector4(1f, 0f, 0f, 0f);
            }
            return splat;
        }

        private static float EstimateSlope(float[,] heights, int x, int z)
        {
            int w = heights.GetLength(0);
            int h = heights.GetLength(1);
            int x0 = Math.Clamp(x - 1, 0, w - 1);
            int x1 = Math.Clamp(x + 1, 0, w - 1);
            int z0 = Math.Clamp(z - 1, 0, h - 1);
            int z1 = Math.Clamp(z + 1, 0, h - 1);
            float dx = heights[x1, z] - heights[x0, z];
            float dz = heights[x, z1] - heights[x, z0];
            return MathF.Min(1f, MathF.Sqrt(dx * dx + dz * dz) * 0.5f);
        }
    }
}
