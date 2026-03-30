using System.Drawing;
using System.Text.Json.Serialization;

namespace Veilborne.Core.Biomes
{
    /// <summary>RGBA colour entry as stored in JSON (0-255 per channel).</summary>
    public class ColorRgba
    {
        public byte R { get; set; } = 255;
        public byte G { get; set; } = 255;
        public byte B { get; set; } = 255;
        public byte A { get; set; } = 255;
    }

    public class ColorPaletteData
    {
        public ColorRgba? Primary { get; set; }
        public ColorRgba? Secondary { get; set; }
        public ColorRgba? Accent { get; set; }
    }

    public class BiomeData
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public ProceduralData ProceduralData { get; set; } = new();
        public List<SurfaceTextureLayer>? SurfaceTextures { get; set; }
        public Dictionary<string, TextureRule>? TextureRules { get; set; }
        public TerrainLayerConfig TerrainLayers { get; set; } = new();
        public BiomeMiningConfig Mining { get; set; } = new();

        /// <summary>Data-driven stacked noise layers for rich terrain variation beyond base modifiers.</summary>
        public List<NoiseLayerConfig>? NoiseLayers { get; set; }

        public List<string> DominantFlora { get; set; } = new();
        public List<string> DominantFauna { get; set; } = new();

        /// <summary>Deserialized from JSON's ColorPalette block.</summary>
        public ColorPaletteData? ColorPalette { get; set; }

        /// <summary>Primary biome colour, derived from ColorPalette.Primary if present.</summary>
        [JsonIgnore]
        public Color Color => ColorPalette?.Primary is { } p
            ? Color.FromArgb(p.A, p.R, p.G, p.B)
            : Color.Green;

        public float BaseHeight { get; set; } = 0f;
        public float HeightMultiplier { get; set; } = 1f;

        // Optional properties
        public List<string>? AllowedObjects { get; set; }
        public string? SurfaceMaterial { get; set; }
        public List<string>? WeatherPatterns { get; set; }
        public List<string>? AssetTags { get; set; }
        public float? LightingModifier { get; set; }
        public string? FeatureDescription { get; set; }
        public List<string>? SpecialFeatures { get; set; }
        public string? MusicTag { get; set; }
    }

    public sealed class BiomeMiningConfig
    {
        public List<BiomeOreRule> Ores { get; set; } = new();
    }

    public sealed class BiomeOreRule
    {
        public string OreType { get; set; } = "coal";
        public float OreNoiseFrequency { get; set; } = 6.0f;
        public float OreThreshold { get; set; } = 0.72f;
        public float OreMinDepth { get; set; } = 0.35f;
        public float OreMaxDepth { get; set; } = 1.2f;
        public float OreSpawnChance { get; set; } = 0.08f;
    }

    public class ProceduralData
    {
        public ProceduralBase Base { get; set; } = new();
        public ProceduralWeights Weights { get; set; } = new();
        public BiomeNoiseModifiers NoiseModifiers { get; set; } = new();
    }

    public class ProceduralBase
    {
        public float Temperature { get; set; }
        public float Moisture { get; set; }
        public float Altitude { get; set; }
        public float Fertility { get; set; }
        public float Roughness { get; set; }
        public float VegetationDensity { get; set; }
    }

    public class ProceduralWeights
    {
        public float WtTemp { get; set; } = 1f;
        public float WtMoisture { get; set; } = 1f;
        public float WtElevation { get; set; } = 1f;
        public float WtFertility { get; set; } = 1f;
    }

    public class BiomeNoiseModifiers
    {
        public float HeightScale { get; set; } = 0f;
        public float Frequency { get; set; } = 1f;
        public float Persistence { get; set; } = 0.5f;
        public float Lacunarity { get; set; } = 2f;
        public float Detail { get; set; } = 0f;

        /// <summary>Weight of ridge noise vs smooth FBM (0 = pure FBM, 1 = full ridges).</summary>
        public float RidgeWeight { get; set; } = 0.5f;

        /// <summary>Weight of billow (abs FBM) noise for puffy/rounded terrain shapes.</summary>
        public float BillowWeight { get; set; } = 0f;

        /// <summary>Sharpness exponent for ridge noise (higher = sharper peaks).</summary>
        public float RidgeSharpness { get; set; } = 1.5f;

        /// <summary>Domain warping strength for organic terrain shapes.</summary>
        public float WarpStrength { get; set; } = 0f;

        /// <summary>Frequency of domain warp noise.</summary>
        public float WarpFrequency { get; set; } = 0.6f;

        /// <summary>Flattens terrain above this normalised height (0 = disabled, 0.7 = plateau above 70%).</summary>
        public float PlateauLevel { get; set; } = 0f;

        /// <summary>How much terrain is terraced/stepped (0 = smooth, 1 = heavy terracing).</summary>
        public float TerracingStrength { get; set; } = 0f;

        /// <summary>Number of terrace steps when TerracingStrength > 0.</summary>
        public int TerracingSteps { get; set; } = 6;

        /// <summary>Erosion simulation strength — smooths ridges and deepens valleys.</summary>
        public float ErosionStrength { get; set; } = 0f;

        /// <summary>Additional frequency multiplier for micro-detail noise layer.</summary>
        public float MicroDetailFrequency { get; set; } = 0f;

        /// <summary>Amplitude of the micro-detail noise layer (adds fine surface variation).</summary>
        public float MicroDetailAmplitude { get; set; } = 0f;

        /// <summary>Slope-driven erosion scaling: steeper areas get more erosion (0 = uniform, 1 = slope-only).</summary>
        public float SlopeErosionScale { get; set; } = 0f;

        /// <summary>Biases valleys deeper (positive) or shallower (negative). Range [-1, 1].</summary>
        public float ValleyDepthBias { get; set; } = 0f;

        // ── Continental / macro-scale noise ──────────────────────

        /// <summary>Amplitude of very low-frequency continental height variation (mountain ranges vs plains).</summary>
        public float ContinentalScale { get; set; } = 0f;

        /// <summary>Frequency multiplier for the continental noise layer (lower = larger features).</summary>
        public float ContinentalFrequency { get; set; } = 0.15f;

        // ── Dune / directional noise ─────────────────────────────

        /// <summary>Frequency of dune-pattern noise (wind-sculpted sand dunes, rolling hills).</summary>
        public float DuneFrequency { get; set; } = 0f;

        /// <summary>Height amplitude of dune patterns.</summary>
        public float DuneAmplitude { get; set; } = 0f;

        /// <summary>Wind direction for dune alignment in degrees (0 = north, 90 = east).</summary>
        public float DuneDirection { get; set; } = 45f;

        // ── Crater / volcanic features ───────────────────────────

        /// <summary>Frequency of impact-crater-like depressions (Worley/cellular noise).</summary>
        public float CraterFrequency { get; set; } = 0f;

        /// <summary>Depth of crater depressions.</summary>
        public float CraterDepth { get; set; } = 0f;

        /// <summary>Rim height around craters (raised lip).</summary>
        public float CraterRimHeight { get; set; } = 0f;

        // ── Overhang / cliff features ────────────────────────────

        /// <summary>Strength of overhang-like cliff protrusion (0 = none, 1 = heavy overhangs).</summary>
        public float OverhangStrength { get; set; } = 0f;

        /// <summary>Vertical frequency of overhang layering.</summary>
        public float OverhangFrequency { get; set; } = 1f;
    }

    /// <summary>
    /// A data-driven noise layer that gets stacked on top of the base terrain.
    /// Allows biome designers to add arbitrary noise without code changes.
    /// </summary>
    public class NoiseLayerConfig
    {
        /// <summary>Noise algorithm: Perlin, Ridge, Billow, Value, Worley.</summary>
        public string Type { get; set; } = "Perlin";

        /// <summary>Spatial frequency of this noise layer.</summary>
        public float Frequency { get; set; } = 1f;

        /// <summary>Height contribution of this noise layer.</summary>
        public float Amplitude { get; set; } = 1f;

        /// <summary>Number of octaves for fractal detail (1-8).</summary>
        public int Octaves { get; set; } = 4;

        /// <summary>Amplitude decay per octave (0-1, typically 0.5).</summary>
        public float Persistence { get; set; } = 0.5f;

        /// <summary>Frequency increase per octave (typically 2.0).</summary>
        public float Lacunarity { get; set; } = 2f;

        /// <summary>How this layer combines with existing terrain: Add, Multiply, Max, Min, Screen.</summary>
        public string BlendMode { get; set; } = "Add";

        /// <summary>Constant added to noise output before blending.</summary>
        public float Offset { get; set; } = 0f;

        /// <summary>Seed offset for this layer (0 = derive from biome seed).</summary>
        public int Seed { get; set; } = 0;

        /// <summary>Toggle to enable/disable this layer without removing it.</summary>
        public bool Enabled { get; set; } = true;
    }
}
