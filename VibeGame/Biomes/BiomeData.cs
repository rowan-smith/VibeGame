using System;
using System.Collections.Generic;

namespace VibeGame.Biomes
{
    // Represents a single RGBA color in a biome
    public struct BiomeColor
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        public BiomeColor(byte r, byte g, byte b, byte a = 255)
        {
            R = r; G = g; B = b; A = a;
        }

        // Parse from hex string (#RRGGBB or #RRGGBBAA)
        public static BiomeColor FromHex(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return new BiomeColor(255, 255, 255, 255);
            if (hex[0] == '#') hex = hex.Substring(1);

            if (hex.Length == 6)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                return new BiomeColor(r, g, b, 255);
            }
            if (hex.Length == 8)
            {
                byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                byte a = Convert.ToByte(hex.Substring(6, 2), 16);
                return new BiomeColor(r, g, b, a);
            }
            return new BiomeColor(255, 255, 255, 255);
        }
    }

    // Basic color palette for a biome
    public struct ColorPalette
    {
        public BiomeColor Primary { get; set; }    // Main color (terrain, grass, etc.)
        public BiomeColor Secondary { get; set; }  // Secondary color (foliage, rocks)
        public BiomeColor Accent { get; set; }     // Accent for rare features or highlights

        public ColorPalette(BiomeColor primary, BiomeColor secondary, BiomeColor accent)
        {
            Primary = primary;
            Secondary = secondary;
            Accent = accent;
        }
    }

    // Noise modifiers for procedural generation
    public struct BiomeNoiseModifiers
    {
        public float HeightScale { get; set; }     // Vertical exaggeration of terrain
        public float Frequency { get; set; }       // Noise frequency
        public float Persistence { get; set; }     // Controls amplitude falloff for successive octaves
        public float Lacunarity { get; set; }      // Controls frequency increase for successive octaves
        public float Detail { get; set; }          // Fine detail layer multiplier

        // Default noise values
        public static BiomeNoiseModifiers Default => new BiomeNoiseModifiers
        {
            HeightScale = 0.0f,
            Frequency = 1.0f,
            Persistence = 0.5f,
            Lacunarity = 2.0f,
            Detail = 0.0f,
        };
    }

    // Base procedural biome parameters
    public sealed class ProceduralBase
    {
        public float Temperature { get; set; }       // 0..1 scale
        public float Moisture { get; set; }          // 0..1 scale
        public float Altitude { get; set; }          // Base altitude
        public float Fertility { get; set; }         // Vegetation fertility
        public float Roughness { get; set; }         // Terrain ruggedness
        public float VegetationDensity { get; set; } // Density of plants/trees
    }

    // Weights for procedural calculation (importance of each factor)
    public sealed class ProceduralWeights
    {
        public float WtTemp { get; set; } = 1f;      // Weight of temperature in biome placement
        public float WtMoisture { get; set; } = 1f;  // Weight of moisture
        public float WtElevation { get; set; } = 1f; // Weight of elevation
        public float WtFertility { get; set; } = 1f; // Weight of fertility
    }

    // Procedural data wrapper for a biome
    public sealed class ProceduralData
    {
        public ProceduralBase Base { get; set; } = new();        // Core biome parameters
        public ProceduralWeights Weights { get; set; } = new();  // Influence of each parameter
        public BiomeNoiseModifiers NoiseModifiers { get; set; } = BiomeNoiseModifiers.Default; // Noise modifiers
    }

    // Full biome configuration
    public class BiomeData
    {
        public string Id { get; set; } = string.Empty;                 // Unique biome ID: "verdigris_expanse"
        public string DisplayName { get; set; } = string.Empty;        // Friendly name
        public bool Enabled { get; set; } = true;                      // Can this biome spawn?

        public ProceduralData ProceduralData { get; set; } = new();    // Procedural generation data
        public ColorPalette ColorPalette { get; set; }                 // Visual colors for terrain/foliage

        public List<string> DominantFlora { get; set; } = new();       // e.g., ["maple_tree", "birch_tree"]
        public List<string> DominantFauna { get; set; } = new();       // e.g., ["deer", "fox"]
        public string SurfaceMaterial { get; set; } = string.Empty;    // Primary ground material: "grass", "dirt", "sand"
        public List<string> WeatherPatterns { get; set; } = new();     // e.g., ["rain", "fog"]
        public List<string> AssetTags { get; set; } = new();           // Used for filtering/asset grouping

        public float LightingModifier { get; set; } = 1f;              // Brightness multiplier for biome
        public string FeatureDescription { get; set; } = string.Empty; // Optional text describing biome
        public List<string> SpecialFeatures { get; set; } = new();    // Rare or special objects/features
        public string? MusicTag { get; set; }                          // Optional background music
        public List<string> AllowedObjects { get; set; } = new();     // WorldObjects allowed to spawn in this biome
    }
}
