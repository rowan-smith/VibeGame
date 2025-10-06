namespace VibeGame.Biomes.Environment
{
    /// Describes a biome as a cluster center in multi-noise space.
    public sealed class BiomeClusterProfile
    {
        public string BiomeId { get; }
        public float Temp { get; }
        public float Moisture { get; }
        public float Elevation { get; }
        public float Fertility { get; }

        // Per-axis weights to shape the cluster distance
        public float WtTemp { get; }
        public float WtMoisture { get; }
        public float WtElevation { get; }
        public float WtFertility { get; }

        public BiomeClusterProfile(
            string biomeId,
            float temp, float moisture, float elevation, float fertility,
            float wtTemp = 1f, float wtMoisture = 1f, float wtElevation = 1f, float wtFertility = 1f)
        {
            BiomeId = biomeId;
            Temp = Clamp01(temp);
            Moisture = Clamp01(moisture);
            Elevation = Clamp01(elevation);
            Fertility = Clamp01(fertility);
            WtTemp = wtTemp;
            WtMoisture = wtMoisture;
            WtElevation = wtElevation;
            WtFertility = wtFertility;
        }

        public float DistanceSquared(EnvironmentSample s)
        {
            float dt = (s.Temperature - Temp);
            float dm = (s.Moisture - Moisture);
            float de = (s.Elevation - Elevation);
            float df = (s.Fertility - Fertility);
            return dt * dt * WtTemp + dm * dm * WtMoisture + de * de * WtElevation + df * df * WtFertility;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
