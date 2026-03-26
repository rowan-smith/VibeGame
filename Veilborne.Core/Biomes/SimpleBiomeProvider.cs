using System.Numerics;
using Serilog;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Biomes
{
    /// <summary>
    /// World-space Voronoi-style biome provider.
    /// Produces large, irregular biome regions that are not tied to chunk size
    /// and remain stable across all chunks. Also computes a secondary biome and
    /// a blend factor internally (for future use) but returns only the primary
    /// biome to satisfy IBiomeProvider.
    /// </summary>
    public class SimpleBiomeProvider : IBiomeProvider
    {
        private readonly List<IBiome> _biomes;
        private readonly int _seed;

        // Average world size of a biome cell in units. Higher => larger regions.
        private readonly float _cellSize;
        // Jitter within a cell [0..1], controls irregular shapes
        private readonly float _jitter;
        private readonly float _warpFrequency;
        private readonly float _warpAmplitude;
        private readonly float _blendWidthWorld;

        private readonly ILogger _logger = Log.ForContext<SimpleBiomeProvider>();

        public SimpleBiomeProvider(
            IEnumerable<IBiome> biomes,
            float averageCellSize = 180f,
            int seed = 1337,
            float jitter = 0.85f,
            float warpFrequencyScale = 1f,
            float warpAmplitudeScale = 1f,
            float blendWidthWorld = 90f)
        {
            _biomes = new List<IBiome>(biomes);
            _seed = seed;
            _cellSize = MathF.Max(16f, averageCellSize);
            _jitter = Math.Clamp(jitter, 0f, 1f);
            _warpFrequency = (1f / MathF.Max(32f, _cellSize * 1.35f)) * MathF.Max(0.1f, warpFrequencyScale);
            _warpAmplitude = (_cellSize * 0.38f) * MathF.Max(0f, warpAmplitudeScale);
            _blendWidthWorld = MathF.Max(1f, blendWidthWorld);

            _logger.Debug("Registered {BiomeCount} biomes with avg cell size {CellSize}", _biomes.Count, _cellSize);
        }

        public IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain)
        {
            if (_biomes.Count == 0)
                throw new InvalidOperationException("No biomes registered");

            var warped = Warp(worldPos);

            // Compute nearest Voronoi site. Use a wider neighborhood because domain warping can
            // push effective nearest sites across adjacent-cell boundaries.
            int cx = (int)MathF.Floor(warped.X / _cellSize);
            int cy = (int)MathF.Floor(warped.Y / _cellSize);

            float bestDist = float.MaxValue;
            int bestSX = 0, bestSY = 0;

            // Track second best to derive potential blend (future use)
            float secondDist = float.MaxValue;

            const int searchRadius = 2; // 5x5 neighborhood
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    int sx = cx + dx;
                    int sy = cy + dy;

                    var site = GetSiteWorldPosition(sx, sy);

                    float dxw = warped.X - site.X;
                    float dyw = warped.Y - site.Y;

                    // Per-cell scale to introduce variable sizes
                    float scale = 0.75f + 0.5f * Hash01(sx, sy, 7919);
                    float dist = (dxw * dxw + dyw * dyw) * (scale * scale);

                    if (dist < bestDist)
                    {
                        secondDist = bestDist;
                        bestDist = dist;
                        bestSX = sx; bestSY = sy;
                    }
                    else if (dist < secondDist)
                    {
                        secondDist = dist;
                    }
                }
            }

            // Map the winning site to a biome index deterministically
            int idx = HashToBiomeIndex(bestSX, bestSY);
            return _biomes[idx];
        }

        public (IBiome primary, IBiome? secondary, float secondaryBlend) GetBiomeBlendAt(Vector2 worldPos, ITerrainGenerator terrain)
        {
            if (_biomes.Count == 0)
                throw new InvalidOperationException("No biomes registered");

            var warped = Warp(worldPos);
            int cx = (int)MathF.Floor(warped.X / _cellSize);
            int cy = (int)MathF.Floor(warped.Y / _cellSize);

            float bestDist = float.MaxValue;
            float secondDist = float.MaxValue;
            int bestSX = 0, bestSY = 0;
            int secondSX = 0, secondSY = 0;

            const int searchRadius = 2;
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int sx = cx + dx;
                int sy = cy + dy;
                var site = GetSiteWorldPosition(sx, sy);
                float dxw = warped.X - site.X;
                float dyw = warped.Y - site.Y;
                float scale = 0.75f + 0.5f * Hash01(sx, sy, 7919);
                float dist = MathF.Sqrt((dxw * dxw + dyw * dyw) * (scale * scale));

                if (dist < bestDist)
                {
                    secondDist = bestDist;
                    secondSX = bestSX; secondSY = bestSY;
                    bestDist = dist;
                    bestSX = sx; bestSY = sy;
                }
                else if (dist < secondDist)
                {
                    secondDist = dist;
                    secondSX = sx; secondSY = sy;
                }
            }

            var primary = _biomes[HashToBiomeIndex(bestSX, bestSY)];
            IBiome? secondary = null;
            float blend = 0f;
            if (secondDist < float.MaxValue && secondDist > bestDist)
            {
                secondary = _biomes[HashToBiomeIndex(secondSX, secondSY)];
                float delta = secondDist - bestDist;
                blend = 1f - Math.Clamp(delta / _blendWidthWorld, 0f, 1f);
                blend = SmoothStep(blend) * 0.49f;
            }

            if (secondary is null || string.Equals(secondary.Id, primary.Id, StringComparison.OrdinalIgnoreCase))
                return (primary, null, 0f);
            return (primary, secondary, blend);
        }

        private Vector2 Warp(Vector2 p)
        {
            float nx = p.X * _warpFrequency;
            float ny = p.Y * _warpFrequency;
            float wx = (HashNoise(nx + 19.31f, ny - 7.73f, 31337) - 0.5f) * 2f;
            float wy = (HashNoise(nx - 11.07f, ny + 5.41f, 73331) - 0.5f) * 2f;
            return new Vector2(p.X + wx * _warpAmplitude, p.Y + wy * _warpAmplitude);
        }

        private int HashToBiomeIndex(int sx, int sy)
        {
            unchecked
            {
                int h = _seed;
                h = (h * 16777619) ^ sx;
                h = (h * 16777619) ^ sy;
                if (h < 0) h = ~h;
                return h % _biomes.Count;
            }
        }

        private Vector2 GetSiteWorldPosition(int sx, int sy)
        {
            // Center of the cell
            float baseX = (sx + 0.5f) * _cellSize;
            float baseY = (sy + 0.5f) * _cellSize;

            // Jitter inside the cell for irregular shapes
            float jx = (Hash01(sx, sy, 1013) - 0.5f) * 2f * _jitter * (_cellSize * 0.5f);
            float jy = (Hash01(sx, sy, 3253) - 0.5f) * 2f * _jitter * (_cellSize * 0.5f);

            return new Vector2(baseX + jx, baseY + jy);
        }

        private float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                int h = _seed ^ salt;
                h = (h * 374761393) ^ x;
                h = (h * 668265263) ^ y;
                h ^= h >> 13;
                h *= 1274126177;
                // Convert to [0,1)
                uint u = (uint)h;
                return (u & 0xFFFFFF) / (float)0x1000000; // 24 bits precision
            }
        }

        private float HashNoise(float x, float y, int salt)
        {
            int ix = (int)MathF.Floor(x);
            int iy = (int)MathF.Floor(y);
            float tx = x - ix;
            float ty = y - iy;

            float h00 = Hash01(ix, iy, salt);
            float h10 = Hash01(ix + 1, iy, salt);
            float h01 = Hash01(ix, iy + 1, salt);
            float h11 = Hash01(ix + 1, iy + 1, salt);

            float sx = SmoothStep(tx);
            float sy = SmoothStep(ty);
            float nx0 = h00 + (h10 - h00) * sx;
            float nx1 = h01 + (h11 - h01) * sx;
            return nx0 + (nx1 - nx0) * sy;
        }

        private static float SmoothStep(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
