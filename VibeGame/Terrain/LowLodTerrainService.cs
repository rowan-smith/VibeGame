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
            foreach (var kv in _heights)
            {
                var key = kv.Key;
                int dx = key.cx;
                int dz = key.cz;

                // Skip inner exclusion if any
                // Determine center relative to camera chunk to compute distance
                // (We rely on UpdateAround to have skipped inner already, but keep a safety check)
                // No camera reference here; compute based on last radius is sufficient

                float chunkWorldSize = (_chunkSize - 1) * _tileSize;
                Vector2 origin = new Vector2(dx * chunkWorldSize, dz * chunkWorldSize);

                // Apply biome textures for this chunk before rendering
                var biome = _biomeProvider.GetBiomeAt(origin, _gen);
                _renderer.ApplyBiomeTextures(biome.Data);
                _renderer.RenderAt(kv.Value, _tileSize * _lodStride, origin, camera, baseColor);
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
