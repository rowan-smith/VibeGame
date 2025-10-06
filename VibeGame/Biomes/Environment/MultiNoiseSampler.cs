using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Environment
{
    /// Samples multi-dimensional noise fields to produce environment variables.
    /// - Temperature: broad low-frequency noise (with mild equator-like gradient optional in future)
    /// - Moisture: independent low-frequency noise
    /// - Fertility: derived from moisture and an independent component
    /// - Elevation: uses terrain.ComputeHeight normalized to configured range
    public sealed class MultiNoiseSampler : IEnvironmentSampler
    {
        private readonly MultiNoiseConfig _cfg;
        private readonly INoiseSource _warpA;
        private readonly INoiseSource _warpB;
        private readonly INoiseSource _tempNoise;
        private readonly INoiseSource _moistNoise;
        private readonly INoiseSource _fertNoise;

        public MultiNoiseSampler(MultiNoiseConfig? cfg = null)
        {
            _cfg = cfg ?? new MultiNoiseConfig();
            int s = _cfg.Seed;

            _warpA = new FastNoiseLiteSource(s + 1, FastNoiseLite.NoiseType.OpenSimplex2, _cfg.WarpFrequency, 2, 2.0f, 0.5f);
            _warpB = new FastNoiseLiteSource(s + 2, FastNoiseLite.NoiseType.OpenSimplex2, _cfg.WarpFrequency, 2, 2.0f, 0.5f);
            _tempNoise  = new FastNoiseLiteSource(s + 10, FastNoiseLite.NoiseType.OpenSimplex2, _cfg.TemperatureFrequency, 3, 2.0f, 0.5f);
            _moistNoise = new FastNoiseLiteSource(s + 20, FastNoiseLite.NoiseType.OpenSimplex2, _cfg.MoistureFrequency,   3, 2.0f, 0.5f);
            _fertNoise  = new FastNoiseLiteSource(s + 30, FastNoiseLite.NoiseType.OpenSimplex2S, _cfg.FertilityFrequency, 3, 2.2f, 0.55f);
        }

        public EnvironmentSample Sample(Vector2 worldPos, ITerrainGenerator terrain)
        {
            // Domain warp space to avoid straight contours
            float w = _cfg.WarpAmount;
            float wa = _warpA.GetValue3D(worldPos.X + 200f, 0f, worldPos.Y + 200f);
            float wb = _warpB.GetValue3D(worldPos.X - 200f, 0f, worldPos.Y - 200f);
            float wx = worldPos.X + (((wa + 1f) * 0.5f) - 0.5f) * w;
            float wz = worldPos.Y + (((wb + 1f) * 0.5f) - 0.5f) * w;

            // Sample noises in [-1,1] then map to [0,1]
            float t = To01(_tempNoise.GetValue3D(wx, 0f, wz));
            float m = To01(_moistNoise.GetValue3D(wx + 333f, 0f, wz - 777f));

            // Slight correlation: fertility benefits from moisture but has independent structure
            float fInd = To01(_fertNoise.GetValue3D(wx - 555f, 0f, wz + 111f));
            float f = Clamp01(0.65f * m + 0.35f * fInd);

            // Elevation from terrain (world units) -> normalized 0..1 via expected range
            float h = terrain.ComputeHeight(worldPos.X, worldPos.Y);
            float e = Normalize(h, _cfg.ElevationMin, _cfg.ElevationMax);

            return new EnvironmentSample(t, m, e, f);
        }

        private static float To01(float v) => (v + 1f) * 0.5f;
        private static float Normalize(float v, float vmin, float vmax)
        {
            if (vmax <= vmin) return 0f;
            float t = (v - vmin) / (vmax - vmin);
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
