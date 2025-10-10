using System.Drawing;

namespace VibeGame.Biomes
{
    public class BiomeData
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public ProceduralData ProceduralData { get; set; } = new();
        public List<SurfaceTextureLayer>? SurfaceTextures { get; set; }
        public Dictionary<string, TextureRule>? TextureRules { get; set; }
        public List<string> DominantFlora { get; set; } = new();
        public List<string> DominantFauna { get; set; } = new();
        public Color Color { get; set; } = Color.Green;
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
    }
}
