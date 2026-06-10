namespace Veilborne.Biomes
{
    public class SurfaceTextureLayer
    {
        public string TextureId { get; set; } = string.Empty;
        public float BlendMin { get; set; } = 0f;
        public float BlendMax { get; set; } = 1f;

        /// <summary>Minimum depth at which this texture appears.</summary>
        public float DepthMin { get; set; } = 0f;
        /// <summary>Maximum depth at which this texture appears.</summary>
        public float DepthMax { get; set; } = float.MaxValue;
        /// <summary>Random depth variation per cell for organic transitions.</summary>
        public float DepthVariation { get; set; } = 0f;
        /// <summary>Noise-driven spatial frequency for patchy coverage.</summary>
        public float NoiseScale { get; set; } = 0f;
        /// <summary>Minimum slope (gradient magnitude) for this layer.</summary>
        public float SlopeMin { get; set; } = 0f;
        /// <summary>Maximum slope for this layer.</summary>
        public float SlopeMax { get; set; } = float.MaxValue;

        // ── Climate-gated texture selection ──────────────────────

        /// <summary>Minimum moisture for this texture to appear (0-1). Enables biome-internal moisture variation.</summary>
        public float MoistureMin { get; set; } = 0f;
        /// <summary>Maximum moisture for this texture to appear (0-1).</summary>
        public float MoistureMax { get; set; } = 1f;
        /// <summary>Minimum temperature for this texture to appear (0-1).</summary>
        public float TemperatureMin { get; set; } = 0f;
        /// <summary>Maximum temperature for this texture to appear (0-1).</summary>
        public float TemperatureMax { get; set; } = 1f;

        // ── Coverage and patchiness ──────────────────────────────

        /// <summary>Fraction of the area this texture covers (0-1, 1 = full coverage).</summary>
        public float Coverage { get; set; } = 1f;
        /// <summary>How patchy/noisy the coverage boundary is (0 = smooth edge, 1 = very broken up).</summary>
        public float Patchiness { get; set; } = 0f;
        /// <summary>Blend curve sharpness: 1 = linear, &lt;1 = soft, &gt;1 = hard edge.</summary>
        public float BlendCurveExponent { get; set; } = 1f;

        /// <summary>Priority for layering when multiple textures match conditions (higher = drawn on top).</summary>
        public int Priority { get; set; } = 0;
    }
}
