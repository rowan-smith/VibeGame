namespace VibeGame.Biomes.Environment
{
    public sealed class MultiNoiseConfig
    {
        public int Seed { get; set; } = 12345;
        public float TemperatureFrequency { get; set; } = 0.0055f;
        public float MoistureFrequency { get; set; } = 0.0060f;
        public float FertilityFrequency { get; set; } = 0.0070f;
        public float WarpAmount { get; set; } = 36.0f;
        public float WarpFrequency { get; set; } = 0.0035f;
        public float ElevationMin { get; set; } = 0.0f;
        public float ElevationMax { get; set; } = 12.0f;
        public float WeightTemperature { get; set; } = 1.0f;
        public float WeightMoisture { get; set; } = 1.0f;
        public float WeightElevation { get; set; } = 1.2f;
        public float WeightFertility { get; set; } = 0.8f;
    }
}
