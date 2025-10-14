using System.Numerics;
using Serilog;
using VibeGame.Terrain;

namespace VibeGame.Biomes
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

        private readonly ILogger _logger = Log.ForContext<SimpleBiomeProvider>();

        public SimpleBiomeProvider(IEnumerable<IBiome> biomes, float averageCellSize = 180f, int seed = 1337, float jitter = 0.85f)
        {
            _biomes = new List<IBiome>(biomes);
            _seed = seed;
            _cellSize = MathF.Max(16f, averageCellSize);
            _jitter = Math.Clamp(jitter, 0f, 1f);

            _logger.Debug("Registered {BiomeCount} biomes with avg cell size {CellSize}", _biomes.Count, _cellSize);
        }

        public IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain)
        {
            if (_biomes.Count == 0)
                throw new InvalidOperationException("No biomes registered");

            // Compute nearest Voronoi site among the 3x3 neighborhood of cells
            int cx = (int)MathF.Floor(worldPos.X / _cellSize);
            int cy = (int)MathF.Floor(worldPos.Y / _cellSize);

            float bestDist = float.MaxValue;
            int bestSX = 0, bestSY = 0;

            // Track second best to derive potential blend (future use)
            float secondDist = float.MaxValue;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int sx = cx + dx;
                    int sy = cy + dy;

                    var site = GetSiteWorldPosition(sx, sy);

                    float dxw = worldPos.X - site.X;
                    float dyw = worldPos.Y - site.Y;

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
    }
}
