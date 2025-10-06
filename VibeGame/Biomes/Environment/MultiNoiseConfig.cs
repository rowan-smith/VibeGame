namespace VibeGame.Biomes.Environment
{
    /// Configuration for multi-noise environment sampling.
    public sealed class MultiNoiseConfig
    {
        public int Seed { get; set; } = 12345;

        // Frequencies: lower = broader features
        // Increased defaults so biome changes are visible within a few chunks while still forming regions
        public float TemperatureFrequency { get; set; } = 0.0055f;
        public float MoistureFrequency { get; set; } = 0.0060f;
        public float FertilityFrequency { get; set; } = 0.0070f;

        // Domain warp to reduce grid alignment
        public float WarpAmount { get; set; } = 36.0f;
        public float WarpFrequency { get; set; } = 0.0035f;

        // Expected height range in world units to normalize Elevation 0..1
        public float ElevationMin { get; set; } = 0.0f;
        public float ElevationMax { get; set; } = 12.0f; // matches TerrainGenerator typical range

        // Weighting of variables for cluster distance
        public float WeightTemperature { get; set; } = 1.0f;
        public float WeightMoisture { get; set; } = 1.0f;
        public float WeightElevation { get; set; } = 1.2f;
        public float WeightFertility { get; set; } = 0.8f;
    }
}
