using System.Numerics;
using System.Runtime.CompilerServices;

namespace VibeGame.Biomes.Environment
{
    public readonly struct EnvironmentSample
    {
        public readonly float Temperature;
        public readonly float Moisture;
        public readonly float Elevation;
        public readonly float Fertility;

        public EnvironmentSample(float temperature, float moisture, float elevation, float fertility)
        {
            Temperature = Clamp01(temperature);
            Moisture = Clamp01(moisture);
            Elevation = Clamp01(elevation);
            Fertility = Clamp01(fertility);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public override string ToString() => $"Env(T={Temperature:F2}, M={Moisture:F2}, E={Elevation:F2}, F={Fertility:F2})";
    }
}
