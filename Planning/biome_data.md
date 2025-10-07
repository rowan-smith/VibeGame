

# 🧩 Biome Data Schema

Here’s what each biome can have — these are parameters useful for both procedural generation and aesthetic control:

| Parameter              | Type                                             | Description                                                 |
| ---------------------- | ------------------------------------------------ | ----------------------------------------------------------- |
| **Name**               | `string`                                         | Biome name                                                  |
| **Temperature**        | `float`                                          | Range 0–1 (0 = freezing, 1 = scorching)                     |
| **Moisture**           | `float`                                          | Range 0–1 (0 = arid, 1 = humid)                             |
| **Altitude**           | `float`                                          | Average height above sea level                              |
| **Roughness**          | `float`                                          | How noisy/varied the terrain is                             |
| **VegetationDensity**  | `float`                                          | How dense vegetation appears                                |
| **ColorPalette**       | `(Color Primary, Color Secondary, Color Accent)` | Base colors for terrain/material tinting                    |
| **DominantFlora**      | `List<string>`                                   | Trees, plants, mushrooms etc.                               |
| **DominantFauna**      | `List<string>`                                   | Animal or creature types (for ambient spawning)             |
| **SurfaceMaterial**    | `string`                                         | e.g., “sand”, “rock”, “grass”, “ice”, etc.                  |
| **WeatherPatterns**    | `List<string>`                                   | e.g., “fog”, “rain”, “lightning”, “ashfall”                 |
| **AssetTags**          | `List<string>`                                   | Used to select textures/models only valid for this biome    |
| **LightingModifier**   | `float`                                          | Brightness multiplier (0.5 = dim, 1 = normal, 1.5 = bright) |
| **FeatureDescription** | `string`                                         | Short flavor text for gameplay or UI                        |
| **SpecialFeatures**    | `List<string>`                                   | Landmarks or rare terrain generation features               |
| **MusicTag**           | `string`                                         | Optional soundtrack tag                                     |


🌍 Unique Biomes List (with Example Values)

Below are 15 original biome concepts ready to use — each includes balanced, gameplay-friendly parameters.

1. Verdigris Expanse
   - Temperature: 0.6
   - Moisture: 0.3
   - Altitude: 0.2
   - Roughness: 0.4
   - VegetationDensity: 0.2
   - ColorPalette: Olive Green, Copper, Dust Brown
   - SurfaceMaterial: Corroded Soil
   - DominantFlora: Thornbrush, Oxide Ferns
   - WeatherPatterns: Acid Rain, Wind Gusts
   - AssetTags: ["rust", "metallic-rock", "dry-grass"]
   - FeatureDescription: A corroded wasteland where the soil glows faintly green from mineral decay.
   - SpecialFeatures: Acid pools, metallic ruins


2. Aetherwild Grove
    - Temperature: 0.7
   - Moisture: 0.8
   - Altitude: 0.3
   - Roughness: 0.5
   - VegetationDensity: 0.9
   - ColorPalette: Cyan, Emerald, Lavender
   - SurfaceMaterial: Bioluminescent moss
   - DominantFlora: Glowcap Trees, Luminous Vines
   - WeatherPatterns: Light mist, Sparkling pollen drift
   - AssetTags: ["bioluminescent", "tree", "moss", "vines"]
   - FeatureDescription: A magical jungle pulsing with radiant life, light drifting through the canopy.
   - SpecialFeatures: Floating spores, glowing ponds


3. Shatterglass Desert
   - Temperature: 0.95
   - Moisture: 0.05
   - Altitude: 0.1
   - Roughness: 0.2
   - VegetationDensity: 0.0
   - ColorPalette: Pale Gold, Cyan, White
   - SurfaceMaterial: Crystalline Sand
   - DominantFlora: None
   - WeatherPatterns: Heat shimmer, mirages
   - AssetTags: ["sand", "crystal", "dune"]
   - FeatureDescription: Sunlight fractures through dunes of shimmering glass, blinding those who wander too long.
   - SpecialFeatures: Crystal monoliths, reflective plains


4. Frostveil Tundra
    - Temperature: 0.1
Moisture: 0.4
Altitude: 0.3
Roughness: 0.3
VegetationDensity: 0.05
ColorPalette: Ice Blue, White, Pale Gray
SurfaceMaterial: Snow/Ice
DominantFlora: Frozen Shrubs, Lichen
WeatherPatterns: Blizzards, auroras
AssetTags: ["ice", "snow", "rock"]
FeatureDescription: A frozen land of silence and pale light, where even sound freezes.
SpecialFeatures: Ice caves, glowing crystals


5. Emberroot Basin
Temperature: 0.85
Moisture: 0.2
Altitude: 0.4
Roughness: 0.6
VegetationDensity: 0.3
ColorPalette: Crimson, Black, Amber
SurfaceMaterial: Charred Earth
DominantFlora: Firebloom Shrubs, Ember Trees
WeatherPatterns: Ashfall, heat haze
AssetTags: ["lava", "ash", "charred-rock"]
FeatureDescription: Fire cracks the surface as molten roots pulse beneath scorched ground.
SpecialFeatures: Lava vents, cracked terrain


6. Mistral Dunes
Temperature: 0.8
Moisture: 0.2
Altitude: 0.15
Roughness: 0.3
VegetationDensity: 0.1
ColorPalette: Sand, Teal, Rust
SurfaceMaterial: Fine Sand
DominantFlora: Desert Palms, Thornbloom
WeatherPatterns: Sandstorms
AssetTags: ["sand", "ruins", "wind-eroded"]
FeatureDescription: Endless dunes shaped by whispering winds that seem almost alive.
SpecialFeatures: Wind-carved ruins


7. Obsidian Expanse
Temperature: 0.65
Moisture: 0.1
Altitude: 0.5
Roughness: 0.7
VegetationDensity: 0.05
ColorPalette: Black, Red, Gray
SurfaceMaterial: Volcanic Glass
DominantFlora: None
WeatherPatterns: Ember rain, hot winds
AssetTags: ["obsidian", "lava", "basalt"]
FeatureDescription: A vast sea of cooled glass, reflecting fire and stars alike.
SpecialFeatures: Lava fractures, glowing cracks


8. Echostep Marsh
Temperature: 0.6
Moisture: 0.9
Altitude: 0.05
Roughness: 0.2
VegetationDensity: 0.8
ColorPalette: Teal, Deep Green, Violet
SurfaceMaterial: Wet Mud
DominantFlora: Reeds, Spirit Lilies
WeatherPatterns: Thick fog, faint whispers
AssetTags: ["mud", "swamp", "mist"]
FeatureDescription: A foggy wetland where echoes never quite fade, and lights drift just out of reach.
SpecialFeatures: Floating lights, sinkholes


9. Glimmerfall Ridge
Temperature: 0.5
Moisture: 0.4
Altitude: 0.9
Roughness: 0.8
VegetationDensity: 0.3
ColorPalette: Silver, Azure, Slate
SurfaceMaterial: Shale/Stone
DominantFlora: Cliff Moss, Frost Fern
WeatherPatterns: Snow flurries, cold mist
AssetTags: ["rock", "cliff", "waterfall"]
FeatureDescription: Sheer cliffs where waterfalls glow faintly under moonlight.
SpecialFeatures: Waterfall networks, frozen ledges


10. Bloodpetal Wilds
Temperature: 0.75
Moisture: 0.7
Altitude: 0.2
Roughness: 0.6
VegetationDensity: 1.0
ColorPalette: Scarlet, Black, Deep Green
SurfaceMaterial: Dark Soil
DominantFlora: Bloodvines, Carnivorous Petal Trees
WeatherPatterns: Red mist, heavy rain
AssetTags: ["organic", "vines", "overgrowth"]
FeatureDescription: A lush jungle of beauty and horror, where every flower hungers.
SpecialFeatures: Plant-based predators


11. Solaris Steppe
Temperature: 0.85
Moisture: 0.3
Altitude: 0.25
Roughness: 0.5
VegetationDensity: 0.4
ColorPalette: Gold, Amber, Brown
SurfaceMaterial: Dry Grass
DominantFlora: Goldengrass, Shrubs
WeatherPatterns: Clear skies, strong winds
AssetTags: ["grass", "rock", "savanna"]
FeatureDescription: A sun-drenched plain where heat shimmers on golden waves.
SpecialFeatures: Windmills, scattered ruins


12. Nullscape
Temperature: 0.0
Moisture: 0.0
Altitude: Variable
Roughness: 0.0
VegetationDensity: 0.0
ColorPalette: Black, Gray, White
SurfaceMaterial: None
DominantFlora: None
WeatherPatterns: None
AssetTags: ["void", "abstract", "glitch"]
FeatureDescription: The void between worlds — static hums, and reality unravels.
SpecialFeatures: Floating terrain, distortion