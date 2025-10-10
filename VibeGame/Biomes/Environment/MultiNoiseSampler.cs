using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Environment
{
    public sealed class MultiNoiseSampler : IEnvironmentSampler
    {
        private readonly MultiNoiseConfig _cfg;

        public MultiNoiseSampler(MultiNoiseConfig? cfg = null)
        {
            _cfg = cfg ?? new MultiNoiseConfig();
        }

        public EnvironmentSample Sample(Vector2 worldPos, ITerrainGenerator terrain)
        {
            float t = 0.5f, m = 0.5f, f = 0.5f;
            float e = Normalize(terrain.ComputeHeight(worldPos.X, worldPos.Y), _cfg.ElevationMin, _cfg.ElevationMax);
            return new EnvironmentSample(t, m, e, f);
        }

        private static float Normalize(float v, float vmin, float vmax)
        {
            if (vmax <= vmin) return 0f;
            float t = (v - vmin) / (vmax - vmin);
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }
    }
}
