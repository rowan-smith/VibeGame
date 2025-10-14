namespace VibeGame.Biomes
{
    public class BiomeProviderConfig
    {
        public float AverageCellSize { get; init; } = 180f;
        public float Jitter { get; init; } = 0.85f;
        public int? Seed { get; init; } = null;
    }
}
