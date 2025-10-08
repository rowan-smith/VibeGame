using System.Numerics;
using Raylib_CsLo;
using VibeGame.Biomes;

namespace VibeGame.Terrain
{
    // Far-distance terrain renderer using a coarser mesh to reduce GPU/CPU cost.
    // Designed to be driven by TerrainManager with a large radius and an inner exclusion ring.
    public class LowLodTerrainService : IInfiniteTerrain
    {
        private readonly ITerrainGenerator _gen;
        private readonly ITerrainRenderer _renderer;
        private readonly IBiomeProvider _biomeProvider;

        private readonly Dictionary<(int cx, int cz), float[,]> _heights = new();
        private readonly int _chunkSize;
        private readonly float _tileSize;
        private int _renderRadiusChunks = 8;

        // We draw a coarser mesh by sampling every N tiles
        private readonly int _lodStride;

        // Prevent double draw with the read-only ring; TerrainManager should set this each frame
        public int InnerExclusionRadiusChunks { get; set; } = 0;

        public LowLodTerrainService(ITerrainGenerator gen, ITerrainRenderer renderer, IBiomeProvider biomeProvider)
        {
            _gen = gen;
            _renderer = renderer;
            _biomeProvider = biomeProvider;
            _chunkSize = gen.TerrainSize;
            _tileSize = gen.TileSize;
            _lodStride = 4; // 4x coarser by default (configurable later)
        }

        public float TileSize => _tileSize * _lodStride; // effective world spacing per height sample
        public int ChunkSize => Math.Max(2, (_chunkSize + _lodStride - 1) / _lodStride);

        private (int cx, int cz) WorldToChunk(float x, float z)
        {
            float chunkWorld = (_chunkSize - 1) * _tileSize;
            int cx = (int)MathF.Floor(x / chunkWorld);
            int cz = (int)MathF.Floor(z / chunkWorld);
            return (cx, cz);
        }

        public void UpdateAround(Vector3 worldPos, int radiusChunks)
        {
            _renderRadiusChunks = radiusChunks;
            var (ccx, ccz) = WorldToChunk(worldPos.X, worldPos.Z);

            // Desired set around the player
            var desired = new HashSet<(int cx, int cz)>();
            for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
            {
                for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
                {
                    int ax = Math.Abs(dx);
                    int az = Math.Abs(dz);
                    int manhattan = Math.Max(ax, az);
                    if (manhattan <= InnerExclusionRadiusChunks) continue; // skip inner area; handled by mid ring
                    desired.Add((ccx + dx, ccz + dz));
                }
            }

            foreach (var key in desired)
            {
                if (_heights.ContainsKey(key)) continue;
                var fine = _gen.GenerateHeightsForChunk(key.cx, key.cz, _chunkSize);
                // Apply the same biome-dependent overlay used in ReadOnlyTerrainService so rings align
                float chunkWorldSize = (_chunkSize - 1) * _tileSize;
                Vector2 origin = new Vector2(key.cx * chunkWorldSize, key.cz * chunkWorldSize);
                var biome = _biomeProvider.GetBiomeAt(origin, _gen);
                var mods = biome.Data.ProceduralData.NoiseModifiers;
                if (mods.HeightScale != 0f)
                {
                    int octaves = Math.Clamp(1 + (int)MathF.Round(mods.Detail * 5f), 1, 6);
                    float baseFreq = 0.03f;
                    float freq = baseFreq * (mods.Frequency <= 0f ? 1f : mods.Frequency);
                    float lac = mods.Lacunarity <= 0f ? 2.0f : mods.Lacunarity;
                    float gain = mods.Persistence;

                    int seed = HashCode.Combine(VibeGame.Core.WorldGlobals.Seed, biome.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), 9176);
                    var overlay = new FastNoiseLiteSource(seed, FastNoiseLite.NoiseType.OpenSimplex2, freq, octaves, lac, gain);

                    for (int z = 0; z < _chunkSize; z++)
                    {
                        for (int x = 0; x < _chunkSize; x++)
                        {
                            float wx = origin.X + x * _tileSize;
                            float wz = origin.Y + z * _tileSize;
                            float n = overlay.GetValue3D(wx, 0f, wz);
                            float delta = n * (mods.HeightScale * 6.0f);
                            fine[x, z] += delta;
                        }
                    }
                }
                var coarse = Downsample(fine, _lodStride);
                _heights[key] = coarse;
            }

            // Simple eviction for far outside of radius
            var keys = _heights.Keys.ToArray();
            foreach (var key in keys)
            {
                if (Math.Max(Math.Abs(key.cx - ccx), Math.Abs(key.cz - ccz)) > radiusChunks + 2)
                    _heights.Remove(key);
            }
        }

        public float SampleHeight(float worldX, float worldZ)
        {
            // Coarse sampling using our cached (or freshly generated) data
            var (cx, cz) = WorldToChunk(worldX, worldZ);
            if (!_heights.TryGetValue((cx, cz), out var h))
            {
                var fine = _gen.GenerateHeightsForChunk(cx, cz, _chunkSize);
                // Apply biome overlay like ReadOnlyTerrainService to match visual terrain
                float chunkWorldSize = (_chunkSize - 1) * _tileSize;
                Vector2 origin = new Vector2(cx * chunkWorldSize, cz * chunkWorldSize);
                var biome = _biomeProvider.GetBiomeAt(origin, _gen);
                var mods = biome.Data.ProceduralData.NoiseModifiers;
                if (mods.HeightScale != 0f)
                {
                    int octaves = Math.Clamp(1 + (int)MathF.Round(mods.Detail * 5f), 1, 6);
                    float baseFreq = 0.03f;
                    float freq = baseFreq * (mods.Frequency <= 0f ? 1f : mods.Frequency);
                    float lac = mods.Lacunarity <= 0f ? 2.0f : mods.Lacunarity;
                    float gain = mods.Persistence;

                    int seed = HashCode.Combine(VibeGame.Core.WorldGlobals.Seed, biome.Id.GetHashCode(StringComparison.OrdinalIgnoreCase), 9176);
                    var overlay = new FastNoiseLiteSource(seed, FastNoiseLite.NoiseType.OpenSimplex2, freq, octaves, lac, gain);

                    for (int z = 0; z < _chunkSize; z++)
                    {
                        for (int x = 0; x < _chunkSize; x++)
                        {
                            float wx = origin.X + x * _tileSize;
                            float wz = origin.Y + z * _tileSize;
                            float n = overlay.GetValue3D(wx, 0f, wz);
                            float delta = n * (mods.HeightScale * 6.0f);
                            fine[x, z] += delta;
                        }
                    }
                }
                h = Downsample(fine, _lodStride);
                _heights[(cx, cz)] = h;
            }

            // Find local indices within the chunk at coarse resolution
            float chunkWorld = (_chunkSize - 1) * _tileSize;
            float localX = worldX - cx * chunkWorld;
            float localZ = worldZ - cz * chunkWorld;
            int ix = (int)Math.Clamp(MathF.Round(localX / (_tileSize * _lodStride)), 0, h.GetLength(0) - 1);
            int iz = (int)Math.Clamp(MathF.Round(localZ / (_tileSize * _lodStride)), 0, h.GetLength(1) - 1);
            return h[ix, iz];
        }

        public IBiome GetBiomeAt(float worldX, float worldZ)
        {
            var (cx, cz) = WorldToChunk(worldX, worldZ);
            float chunkWorldSize = (_chunkSize - 1) * _tileSize;
            Vector2 origin = new Vector2(cx * chunkWorldSize, cz * chunkWorldSize);
            return _biomeProvider.GetBiomeAt(origin, _gen);
        }

        public IEnumerable<(Vector2 center, float radius)> GetNearbyObjectColliders(Vector2 worldPos, float range)
        {
            // Low-LOD ring does not render/own colliders; return empty
            return Array.Empty<(Vector2 center, float radius)>();
        }

        public void Render(Camera3D camera, Color baseColor)
        {
            // Group visible chunks by biome to minimize texture binds and logging in hot paths
            float chunkWorldSize = (_chunkSize - 1) * _tileSize;
            Vector3 camPos = camera.position;
            Vector3 fwd = new Vector3(camera.target.X - camPos.X, 0f, camera.target.Z - camPos.Z);
            if (fwd.LengthSquared() < 0.0001f) fwd = new Vector3(0f, 0f, 1f);
            fwd = Vector3.Normalize(fwd);
            float fovyRad = MathF.PI * (camera.fovy / 180f);
            float cosLimit = MathF.Cos(MathF.Min(1.39626f, fovyRad * 0.75f));

            // Collect visible chunks with their biomes
            var visible = new List<((int cx, int cz) key, Vector2 origin, IBiome biome)>();
            foreach (var kv in _heights)
            {
                var key = kv.Key;
                int dx = key.cx;
                int dz = key.cz;

                Vector2 origin = new Vector2(dx * chunkWorldSize, dz * chunkWorldSize);
                Vector3 center = new Vector3(origin.X + chunkWorldSize * 0.5f, 0f, origin.Y + chunkWorldSize * 0.5f);
                Vector3 toC = new Vector3(center.X - camPos.X, 0f, center.Z - camPos.Z);
                float dist2 = toC.LengthSquared();
                if (dist2 > 1e-4f) toC /= MathF.Sqrt(dist2);
                // Perform approximate frustum culling: skip if largely behind camera and not extremely near
                if (dist2 > (chunkWorldSize * 0.75f) * (chunkWorldSize * 0.75f) && Vector3.Dot(fwd, toC) < cosLimit)
                    continue;

                var biome = _biomeProvider.GetBiomeAt(origin, _gen);
                visible.Add((key, origin, biome));
            }

            // Group by biome id to reduce switching
            foreach (var group in visible.GroupBy(v => v.biome.Id, StringComparer.OrdinalIgnoreCase))
            {
                var first = group.First();
                _renderer.ApplyBiomeTextures(first.biome.Data);
                foreach (var item in group)
                {
                    if (_heights.TryGetValue(item.key, out var heights))
                    {
                        _renderer.RenderAt(heights, _tileSize * _lodStride, item.origin, camera, baseColor);
                    }
                }
            }
        }

        public void RenderDebugChunkBounds(Camera3D camera)
        {
            float chunkWorldSize = (_chunkSize - 1) * _tileSize;
            foreach (var key in _heights.Keys)
            {
                Vector3 min = new Vector3(key.cx * chunkWorldSize, 0, key.cz * chunkWorldSize);
                Vector3 size = new Vector3(chunkWorldSize, 2.0f, chunkWorldSize);
                var color = new Color(120, 120, 220, 160);
                Raylib.DrawCubeWires(min + size * 0.5f, size.X, size.Y, size.Z, color);
            }
        }

        private static float[,] Downsample(float[,] src, int stride)
        {
            int w = src.GetLength(0);
            int h = src.GetLength(1);
            int nw = Math.Max(2, (w + stride - 1) / stride);
            int nh = Math.Max(2, (h + stride - 1) / stride);
            var dst = new float[nw, nh];
            for (int z = 0, oz = 0; z < h && oz < nh; z += stride, oz++)
            {
                for (int x = 0, ox = 0; x < w && ox < nw; x += stride, ox++)
                {
                    dst[ox, oz] = src[x, z];
                }
            }
            return dst;
        }
    }
}
