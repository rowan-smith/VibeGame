namespace Veilborne.Biomes
{
    public class BiomeProviderConfig
    {
        public float AverageCellSize { get; init; } = 300f;
        public float Jitter { get; init; } = 0.85f;
        public int? Seed { get; init; } = null;
    }
}
