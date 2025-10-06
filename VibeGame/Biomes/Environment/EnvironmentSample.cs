using System.Runtime.CompilerServices;

namespace VibeGame.Biomes.Environment
{
    /// Immutable snapshot of environmental variables at a world position.
    /// Values are normalized to 0..1 where possible.
    public readonly struct EnvironmentSample
    {
        public readonly float Temperature; // 0 (cold) .. 1 (hot)
        public readonly float Moisture;    // 0 (dry) .. 1 (wet)
        public readonly float Elevation;   // 0 (low) .. 1 (high)
        public readonly float Fertility;   // 0 (infertile) .. 1 (fertile)

        public EnvironmentSample(float temperature, float moisture, float elevation, float fertility)
        {
            Temperature = Clamp01(temperature);
            Moisture = Clamp01(moisture);
            Elevation = Clamp01(elevation);
            Fertility = Clamp01(fertility);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        public override string ToString()
            => $"Env(T={Temperature:F2}, M={Moisture:F2}, E={Elevation:F2}, F={Fertility:F2})";
    }
}
