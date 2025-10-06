using System.Collections.Generic;

namespace VibeGame.Biomes.Environment
{
    // Unified root config for biomes.json
    public sealed class UnifiedBiomesConfig
    {
        public List<UnifiedBiomeDto> Biomes { get; set; } = new();
    }

    public sealed class UnifiedBiomeDto
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public ProceduralDataDto ProceduralData { get; set; } = new();

        // Metadata section aligns with BiomeData fields
        public ColorPalette ColorPalette { get; set; }
        public List<string> DominantFlora { get; set; } = new();
        public List<string> DominantFauna { get; set; } = new();
        public string SurfaceMaterial { get; set; } = string.Empty;
        public List<string> WeatherPatterns { get; set; } = new();
        public List<string> AssetTags { get; set; } = new();
        public float LightingModifier { get; set; } = 1f;
        public string FeatureDescription { get; set; } = string.Empty;
        public List<string> SpecialFeatures { get; set; } = new();
        public string? MusicTag { get; set; }
        public List<string> AllowedObjects { get; set; } = new();

        public BiomeData ToBiomeData()
        {
            var b = ProceduralData?.Base ?? new BaseProceduralDto();
            var w = ProceduralData?.Weights ?? new WeightsProceduralDto();
            var n = ProceduralData?.NoiseModifiers ?? new NoiseModifiersDto();
            return new BiomeData
            {
                Id = Id,
                DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? Id : DisplayName,
                Enabled = Enabled,
                ProceduralData = new ProceduralData
                {
                    Base = new ProceduralBase
                    {
                        Temperature = b.Temperature,
                        Moisture = b.Moisture,
                        Altitude = b.Altitude,
                        Fertility = b.Fertility,
                        Roughness = b.Roughness,
                        VegetationDensity = b.VegetationDensity
                    },
                    Weights = new ProceduralWeights
                    {
                        WtTemp = w.WtTemp,
                        WtMoisture = w.WtMoisture,
                        WtElevation = w.WtElevation,
                        WtFertility = w.WtFertility
                    },
                    NoiseModifiers = new BiomeNoiseModifiers
                    {
                        HeightScale = n.HeightScale,
                        Frequency = n.Frequency,
                        Persistence = n.Persistence,
                        Lacunarity = n.Lacunarity,
                        Detail = n.Detail
                    }
                },
                ColorPalette = ColorPalette,
                DominantFlora = new List<string>(DominantFlora),
                DominantFauna = new List<string>(DominantFauna),
                SurfaceMaterial = SurfaceMaterial,
                WeatherPatterns = new List<string>(WeatherPatterns),
                AssetTags = new List<string>(AssetTags),
                LightingModifier = LightingModifier,
                FeatureDescription = FeatureDescription,
                SpecialFeatures = new List<string>(SpecialFeatures),
                MusicTag = MusicTag,
                AllowedObjects = new List<string>(AllowedObjects)
            };
        }

        public BiomeClusterProfile ToProfile()
        {
            var b = ProceduralData?.Base ?? new BaseProceduralDto();
            var w = ProceduralData?.Weights ?? new WeightsProceduralDto();
            return new BiomeClusterProfile(
                Id,
                b.Temperature,
                b.Moisture,
                b.Altitude,
                b.Fertility,
                w.WtTemp,
                w.WtMoisture,
                w.WtElevation,
                w.WtFertility
            );
        }
    }

    public sealed class ProceduralDataDto
    {
        public BaseProceduralDto Base { get; set; } = new();
        public WeightsProceduralDto Weights { get; set; } = new();
        public NoiseModifiersDto NoiseModifiers { get; set; } = new();
    }

    public sealed class BaseProceduralDto
    {
        public float Temperature { get; set; }
        public float Moisture { get; set; }
        public float Altitude { get; set; }
        public float Fertility { get; set; }
        public float Roughness { get; set; }
        public float VegetationDensity { get; set; }
    }

    public sealed class WeightsProceduralDto
    {
        public float WtTemp { get; set; } = 1f;
        public float WtMoisture { get; set; } = 1f;
        public float WtElevation { get; set; } = 1f;
        public float WtFertility { get; set; } = 1f;
    }

    public sealed class NoiseModifiersDto
    {
        public float HeightScale { get; set; } = 0f;
        public float Frequency { get; set; } = 1f;
        public float Persistence { get; set; } = 0.5f;
        public float Lacunarity { get; set; } = 2.0f;
        public float Detail { get; set; } = 0f;
    }
}
