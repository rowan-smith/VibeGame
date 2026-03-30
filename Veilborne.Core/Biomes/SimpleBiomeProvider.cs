using System.Numerics;
using System.Collections.Concurrent;
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
        private readonly record struct CellSiteData(Vector2 Site, float Scale, int BiomeIndex);

        private readonly List<IBiome> _biomes;
        private readonly int _seed;

        // Average world size of a biome cell in units. Higher => larger regions.
        private readonly float _cellSize;
        // Jitter within a cell [0..1], controls irregular shapes
        private readonly float _jitter;
        private readonly float _warpFrequency;
        private readonly float _warpAmplitude;
        private readonly float _blendWidthWorld;
        private readonly ConcurrentDictionary<(int sx, int sy), CellSiteData> _cellCache = new();

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

            const int searchRadius = 2; // 5x5 neighborhood
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            {
                for (int dx = -searchRadius; dx <= searchRadius; dx++)
                {
                    int sx = cx + dx;
                    int sy = cy + dy;

                    var cell = GetCellData(sx, sy);
                    var site = cell.Site;

                    float dxw = warped.X - site.X;
                    float dyw = warped.Y - site.Y;

                    float dist = (dxw * dxw + dyw * dyw) * (cell.Scale * cell.Scale);

                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        bestSX = sx; bestSY = sy;
                    }
                }
            }

            // Map the winning site to a biome index deterministically
            return _biomes[GetCellData(bestSX, bestSY).BiomeIndex];
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
                var cell = GetCellData(sx, sy);
                var site = cell.Site;
                float dxw = warped.X - site.X;
                float dyw = warped.Y - site.Y;
                float dist = MathF.Sqrt((dxw * dxw + dyw * dyw) * (cell.Scale * cell.Scale));

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

            var primary = _biomes[GetCellData(bestSX, bestSY).BiomeIndex];
            IBiome? secondary = null;
            float blend = 0f;
            if (secondDist < float.MaxValue && secondDist > bestDist)
            {
                secondary = _biomes[GetCellData(secondSX, secondSY).BiomeIndex];
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

        private int SelectBiomeIndexForSite(int sx, int sy, Vector2 site)
        {
            if (_biomes.Count == 1)
                return 0;

            // Deterministic climate features sampled from site position.
            float temp = HashNoise(site.X * 0.0021f + 19.3f, site.Y * 0.0021f - 7.7f, 12013);
            float moist = HashNoise(site.X * 0.0024f - 3.1f, site.Y * 0.0024f + 11.8f, 17011);
            float elev = HashNoise(site.X * 0.0019f + 5.4f, site.Y * 0.0019f + 2.3f, 23117);
            float fert = HashNoise(site.X * 0.0027f - 9.2f, site.Y * 0.0027f + 0.9f, 29017);

            int best = 0;
            float bestScore = float.PositiveInfinity;
            for (int i = 0; i < _biomes.Count; i++)
            {
                var d = _biomes[i].Data.ProceduralData;
                var b = d.Base;
                var w = d.Weights;
                float score =
                    MathF.Abs(temp - b.Temperature) * MathF.Max(0.0001f, w.WtTemp) +
                    MathF.Abs(moist - b.Moisture) * MathF.Max(0.0001f, w.WtMoisture) +
                    MathF.Abs(elev - b.Altitude) * MathF.Max(0.0001f, w.WtElevation) +
                    MathF.Abs(fert - b.Fertility) * MathF.Max(0.0001f, w.WtFertility);
                // Small deterministic jitter to avoid ties and repetitive boundaries.
                float jitter = (Hash01(sx, sy, 4001 + i * 23) - 0.5f) * 0.02f;
                score += jitter;
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }
            return best;
        }

        private CellSiteData GetCellData(int sx, int sy)
        {
            return _cellCache.GetOrAdd((sx, sy), key =>
            {
                var site = GetSiteWorldPosition(key.sx, key.sy);
                float scale = 0.75f + 0.5f * Hash01(key.sx, key.sy, 7919);
                int biomeIndex = SelectBiomeIndexForSite(key.sx, key.sy, site);
                return new CellSiteData(site, scale, biomeIndex);
            });
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

        /// <summary>
        /// Returns the top-N nearest biomes with inverse-distance blend weights, normalized to 1.0.
        /// Uses smoothstep on the raw weights for organic transitions.
        /// </summary>
        public void GetBlendWeightsAt(Vector2 worldPos, ITerrainGenerator terrain, Span<BiomeWeight> buffer, out int count, int maxResults = 4)
        {
            if (_biomes.Count == 0)
                throw new InvalidOperationException("No biomes registered");

            maxResults = Math.Clamp(maxResults, 1, Math.Min(buffer.Length, 8));
            var warped = Warp(worldPos);
            int cx = (int)MathF.Floor(warped.X / _cellSize);
            int cy = (int)MathF.Floor(warped.Y / _cellSize);

            // Collect all candidates with their distances
            Span<(int sx, int sy, float dist)> candidates = stackalloc (int, int, float)[25];
            int candidateCount = 0;

            const int searchRadius = 2;
            for (int dy = -searchRadius; dy <= searchRadius; dy++)
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                int sx = cx + dx;
                int sy = cy + dy;
                var cell = GetCellData(sx, sy);
                float dxw = warped.X - cell.Site.X;
                float dyw = warped.Y - cell.Site.Y;
                float dist = MathF.Sqrt((dxw * dxw + dyw * dyw) * (cell.Scale * cell.Scale));
                candidates[candidateCount++] = (sx, sy, dist);
            }

            // Sort by distance (simple insertion sort for small N)
            for (int i = 1; i < candidateCount; i++)
            {
                var tmp = candidates[i];
                int j = i - 1;
                while (j >= 0 && candidates[j].dist > tmp.dist)
                {
                    candidates[j + 1] = candidates[j];
                    j--;
                }
                candidates[j + 1] = tmp;
            }

            // Deduplicate by biome index, keeping only the closest distance per biome
            Span<(int biomeIdx, float dist)> unique = stackalloc (int, float)[maxResults];
            int uniqueCount = 0;
            for (int i = 0; i < candidateCount && uniqueCount < maxResults; i++)
            {
                int biomeIdx = GetCellData(candidates[i].sx, candidates[i].sy).BiomeIndex;
                bool dup = false;
                for (int u = 0; u < uniqueCount; u++)
                {
                    if (unique[u].biomeIdx == biomeIdx) { dup = true; break; }
                }
                if (!dup)
                    unique[uniqueCount++] = (biomeIdx, candidates[i].dist);
            }

            if (uniqueCount <= 1)
            {
                int idx = uniqueCount == 1 ? unique[0].biomeIdx : GetCellData(cx, cy).BiomeIndex;
                buffer[0] = new BiomeWeight(_biomes[idx], 1f);
                count = 1;
                return;
            }

            // Inverse-distance weighting with the blend width as falloff
            float nearestDist = unique[0].dist;
            float totalWeight = 0f;
            Span<float> rawWeights = stackalloc float[uniqueCount];
            for (int i = 0; i < uniqueCount; i++)
            {
                float delta = unique[i].dist - nearestDist;
                float t = 1f - Math.Clamp(delta / _blendWidthWorld, 0f, 1f);
                t = SmoothStep(t);
                rawWeights[i] = t;
                totalWeight += t;
            }

            // Normalize and write results, pruning near-zero weights
            count = 0;
            if (totalWeight < 1e-6f)
            {
                buffer[0] = new BiomeWeight(_biomes[unique[0].biomeIdx], 1f);
                count = 1;
                return;
            }

            for (int i = 0; i < uniqueCount; i++)
            {
                float w = rawWeights[i] / totalWeight;
                if (w < 0.005f) continue;
                buffer[count++] = new BiomeWeight(_biomes[unique[i].biomeIdx], w);
            }

            if (count == 0)
            {
                buffer[0] = new BiomeWeight(_biomes[unique[0].biomeIdx], 1f);
                count = 1;
            }
        }

        private static float SmoothStep(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
