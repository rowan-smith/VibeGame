namespace Veilborne.Biomes
{
    public class BiomeProviderConfig
    {
        public float AverageCellSize { get; set; } = 300f;
        public float Jitter { get; set; } = 0.85f;
        public float WarpFrequencyScale { get; set; } = 1f;
        public float WarpAmplitudeScale { get; set; } = 1f;
        public float BlendWidthWorld { get; set; } = 90f;
        public int? Seed { get; set; } = null;
    }
}
