#!/usr/bin/env python3
"""Apply curated fantasy/gamelike terrain recipes to all biome JSON configs."""

import json
import glob

RECIPES = {
    "aetherwild_grove": {
        "desc": "Magical jungle — soft canopy hills, stream valleys, glowing ponds",
        "BaseHeight": 0.0,
        "HeightMultiplier": 1.05,
        "mods": {
            "HeightScale": 0.38, "Frequency": 0.75, "Persistence": 0.42, "Lacunarity": 1.85,
            "Detail": 0.6, "RidgeWeight": 0.22, "BillowWeight": 0.62, "RidgeSharpness": 1.3,
            "WarpStrength": 0.48, "WarpFrequency": 0.5, "ErosionStrength": 0.4,
            "ValleyDepthBias": 0.35, "ContinentalScale": 0.75,
            "CliffStrength": 0.18, "CliffFrequency": 0.38, "CliffSharpness": 0.16, "CliffTiers": 3,
            "CanyonDepth": 0.38, "CanyonFrequency": 0.45, "CanyonWidth": 0.48,
            "PondDepth": 0.55, "PondFrequency": 1.8,
            "RollingHillsAmplitude": 0.35, "RollingHillsFrequency": 0.65,
            "MicroDetailFrequency": 2.4, "MicroDetailAmplitude": 0.16,
        },
        "layers": [
            {"Type": "Billow", "Frequency": 0.55, "Amplitude": 0.6, "Octaves": 4,
             "Persistence": 0.42, "Lacunarity": 1.9, "BlendMode": "Add", "Seed": 11},
            {"Type": "Perlin", "Frequency": 0.95, "Amplitude": 0.32, "Octaves": 3,
             "Persistence": 0.38, "Lacunarity": 2.0, "BlendMode": "Add", "Seed": 23},
            {"Type": "Pond", "Frequency": 1.6, "Amplitude": 0.5, "Octaves": 1,
             "BlendMode": "Add", "Seed": 37},
            {"Type": "Canyon", "Frequency": 0.55, "Amplitude": -0.35, "Octaves": 3,
             "BlendMode": "Add", "Seed": 49},
            {"Type": "Value", "Frequency": 3.2, "Amplitude": 0.1, "Octaves": 2,
             "BlendMode": "Screen", "Seed": 61},
        ],
    },
    "bloodpetal_wilds": {
        "desc": "Horror jungle — twisted tangles, predator pits, dark valleys",
        "BaseHeight": 0.0,
        "HeightMultiplier": 1.12,
        "mods": {
            "HeightScale": 0.42, "Frequency": 1.15, "Persistence": 0.58, "Lacunarity": 2.25,
            "Detail": 0.88, "RidgeWeight": 0.48, "BillowWeight": 0.58, "RidgeSharpness": 2.0,
            "WarpStrength": 0.62, "WarpFrequency": 0.7, "ErosionStrength": 0.22,
            "ValleyDepthBias": 0.55, "ContinentalScale": 0.65,
            "CliffStrength": 0.28, "CliffFrequency": 0.52, "CliffSharpness": 0.1, "CliffTiers": 5,
            "CanyonDepth": 0.62, "CanyonFrequency": 0.72, "CanyonWidth": 0.28,
            "PondDepth": 0.75, "PondFrequency": 2.2,
            "BasinStrength": 0.25,
            "MicroDetailFrequency": 3.2, "MicroDetailAmplitude": 0.22,
        },
        "layers": [
            {"Type": "Billow", "Frequency": 0.85, "Amplitude": 0.7, "Octaves": 5,
             "Persistence": 0.52, "Lacunarity": 2.3, "BlendMode": "Add", "Seed": 13},
            {"Type": "Ridge", "Frequency": 1.35, "Amplitude": 0.42, "Octaves": 4,
             "Persistence": 0.5, "Lacunarity": 2.5, "BlendMode": "Max", "Seed": 29},
            {"Type": "Pond", "Frequency": 2.0, "Amplitude": 0.65, "Octaves": 1,
             "BlendMode": "Add", "Seed": 41},
            {"Type": "Basin", "Frequency": 0.4, "Amplitude": 0.35, "Octaves": 3,
             "BlendMode": "Add", "Seed": 53},
            {"Type": "Canyon", "Frequency": 0.95, "Amplitude": -0.55, "Octaves": 4,
             "BlendMode": "Add", "Seed": 67},
        ],
    },
    "echostep_marsh": {
        "desc": "Foggy wetland — flat basin, sinkholes, echo ripples",
        "BaseHeight": -2.0,
        "HeightMultiplier": 0.48,
        "mods": {
            "HeightScale": 0.08, "Frequency": 0.42, "Persistence": 0.28, "Lacunarity": 1.55,
            "Detail": 0.25, "RidgeWeight": 0.03, "BillowWeight": 0.18, "RidgeSharpness": 1.0,
            "WarpStrength": 0.35, "WarpFrequency": 0.28, "ErosionStrength": 0.85,
            "ValleyDepthBias": 0.7, "ContinentalScale": 0.2,
            "CliffStrength": 0.0, "CanyonDepth": 0.28, "CanyonFrequency": 0.38, "CanyonWidth": 0.58,
            "PondDepth": 0.85, "PondFrequency": 2.5,
            "BasinStrength": 0.72, "WetlandFlatten": 0.82,
            "PlateauLevel": 0.12,
            "MicroDetailFrequency": 1.2, "MicroDetailAmplitude": 0.06,
        },
        "layers": [
            {"Type": "Basin", "Frequency": 0.25, "Amplitude": 0.8, "Octaves": 3,
             "BlendMode": "Add", "Seed": 7},
            {"Type": "Billow", "Frequency": 0.28, "Amplitude": 0.12, "Octaves": 2,
             "Persistence": 0.28, "BlendMode": "Add", "Seed": 19},
            {"Type": "Pond", "Frequency": 2.8, "Amplitude": 0.9, "Octaves": 1,
             "BlendMode": "Add", "Seed": 31},
            {"Type": "Value", "Frequency": 1.8, "Amplitude": 0.08, "Octaves": 2,
             "BlendMode": "Screen", "Seed": 47},
        ],
    },
    "emberroot_basin": {
        "desc": "Volcanic basin — caldera bowl, lava cracks, molten pulse",
        "BaseHeight": 0.8,
        "HeightMultiplier": 1.22,
        "mods": {
            "HeightScale": 0.55, "Frequency": 1.45, "Persistence": 0.62, "Lacunarity": 2.7,
            "Detail": 0.88, "RidgeWeight": 0.68, "BillowWeight": 0.35, "RidgeSharpness": 2.2,
            "WarpStrength": 0.28, "WarpFrequency": 0.55,
            "ContinentalScale": 1.0,
            "CliffStrength": 0.52, "CliffFrequency": 0.62, "CliffSharpness": 0.07, "CliffTiers": 4,
            "CanyonDepth": 0.95, "CanyonFrequency": 0.82, "CanyonWidth": 0.25,
            "CraterFrequency": 1.1, "CraterDepth": 2.2, "CraterRimHeight": 0.9,
            "BasinStrength": 0.45, "VolcanicPulse": 0.85,
            "TerracingStrength": 0.38, "TerracingSteps": 5,
            "MicroDetailFrequency": 3.8, "MicroDetailAmplitude": 0.28,
        },
        "layers": [
            {"Type": "Basin", "Frequency": 0.35, "Amplitude": 0.55, "Octaves": 3,
             "BlendMode": "Add", "Seed": 17},
            {"Type": "Ridge", "Frequency": 1.85, "Amplitude": 0.55, "Octaves": 4,
             "Persistence": 0.55, "Lacunarity": 2.6, "BlendMode": "Max", "Seed": 33},
            {"Type": "Canyon", "Frequency": 1.05, "Amplitude": -0.85, "Octaves": 3,
             "BlendMode": "Add", "Seed": 51},
            {"Type": "Billow", "Frequency": 1.1, "Amplitude": 0.35, "Octaves": 3,
             "BlendMode": "Add", "Seed": 73},
            {"Type": "Cliff", "Frequency": 0.52, "Amplitude": 0.5, "Octaves": 4,
             "BlendMode": "Max", "Seed": 95},
        ],
    },
    "frostveil_tundra": {
        "desc": "Frozen tundra — wind drifts, ice hummocks, pale escarpments",
        "BaseHeight": 0.3,
        "HeightMultiplier": 0.92,
        "mods": {
            "HeightScale": 0.22, "Frequency": 0.62, "Persistence": 0.32, "Lacunarity": 1.75,
            "Detail": 0.38, "RidgeWeight": 0.12, "BillowWeight": 0.48, "RidgeSharpness": 1.2,
            "WarpStrength": 0.22, "WarpFrequency": 0.35, "ErosionStrength": 0.55,
            "ContinentalScale": 1.85, "ContinentalFrequency": 0.07,
            "CliffStrength": 0.22, "CliffFrequency": 0.22, "CliffSharpness": 0.18, "CliffTiers": 3,
            "CanyonDepth": 0.18, "CanyonFrequency": 0.35, "CanyonWidth": 0.52,
            "PondDepth": 0.35, "PondFrequency": 1.4,
            "RollingHillsAmplitude": 0.22, "RollingHillsFrequency": 0.45,
            "MicroDetailFrequency": 2.2, "MicroDetailAmplitude": 0.09,
        },
        "layers": [
            {"Type": "Perlin", "Frequency": 0.22, "Amplitude": 0.35, "Octaves": 3,
             "Persistence": 0.3, "Lacunarity": 1.6, "BlendMode": "Add", "Seed": 9},
            {"Type": "Billow", "Frequency": 0.48, "Amplitude": 0.38, "Octaves": 3,
             "BlendMode": "Add", "Seed": 21},
            {"Type": "Pond", "Frequency": 1.2, "Amplitude": 0.3, "Octaves": 1,
             "BlendMode": "Add", "Seed": 43},
            {"Type": "Value", "Frequency": 2.8, "Amplitude": 0.07, "Octaves": 2,
             "BlendMode": "Screen", "Seed": 61},
        ],
    },
    "glimmerfall_ridge": {
        "desc": "Mountain ridge — sheer cliffs, waterfall gorges, frozen ledges",
        "BaseHeight": 4.0,
        "HeightMultiplier": 1.75,
        "mods": {
            "HeightScale": 0.85, "Frequency": 1.55, "Persistence": 0.78, "Lacunarity": 2.85,
            "Detail": 0.98, "RidgeWeight": 0.94, "BillowWeight": 0.15, "RidgeSharpness": 2.5,
            "WarpStrength": 0.32, "WarpFrequency": 0.48, "ErosionStrength": 0.12,
            "ValleyDepthBias": -0.35, "ContinentalScale": 2.4, "ContinentalFrequency": 0.06,
            "CliffStrength": 0.88, "CliffFrequency": 0.42, "CliffSharpness": 0.04, "CliffTiers": 8,
            "CanyonDepth": 1.65, "CanyonFrequency": 0.68, "CanyonWidth": 0.18,
            "OverhangStrength": 0.78, "OverhangFrequency": 2.2,
            "TerracingStrength": 0.42, "TerracingSteps": 7,
            "MicroDetailFrequency": 2.6, "MicroDetailAmplitude": 0.14,
        },
        "layers": [
            {"Type": "Ridge", "Frequency": 0.75, "Amplitude": 1.0, "Octaves": 5,
             "Persistence": 0.65, "Lacunarity": 2.8, "BlendMode": "Max", "Seed": 15},
            {"Type": "Cliff", "Frequency": 0.48, "Amplitude": 1.15, "Octaves": 6,
             "BlendMode": "Max", "Seed": 27},
            {"Type": "Canyon", "Frequency": 0.78, "Amplitude": -1.2, "Octaves": 4,
             "BlendMode": "Add", "Seed": 39},
            {"Type": "Perlin", "Frequency": 0.55, "Amplitude": 0.4, "Octaves": 3,
             "BlendMode": "Add", "Seed": 55},
            {"Type": "Ridge", "Frequency": 2.5, "Amplitude": 0.25, "Octaves": 2,
             "BlendMode": "Screen", "Seed": 71},
        ],
    },
    "mistral_dunes": {
        "desc": "Living dunes — wind-sculpted barchans, whispering ripples",
        "BaseHeight": -1.2,
        "HeightMultiplier": 0.72,
        "mods": {
            "HeightScale": 0.22, "Frequency": 0.52, "Persistence": 0.28, "Lacunarity": 1.48,
            "Detail": 0.32, "RidgeWeight": 0.08, "BillowWeight": 0.35, "RidgeSharpness": 1.0,
            "WarpStrength": 0.72, "WarpFrequency": 0.38, "ErosionStrength": 0.75,
            "ContinentalScale": 0.48,
            "CliffStrength": 0.04, "CliffTiers": 2,
            "DuneFrequency": 2.8, "DuneAmplitude": 2.1, "DuneDirection": 35,
            "RollingHillsAmplitude": 0.15, "RollingHillsFrequency": 0.35,
            "MicroDetailFrequency": 4.5, "MicroDetailAmplitude": 0.12,
        },
        "layers": [
            {"Type": "Billow", "Frequency": 1.35, "Amplitude": 0.45, "Octaves": 3,
             "Persistence": 0.32, "BlendMode": "Add", "Seed": 12},
            {"Type": "Rolling", "Frequency": 0.55, "Amplitude": 0.2, "Octaves": 2,
             "BlendMode": "Add", "Seed": 28},
            {"Type": "Value", "Frequency": 5.0, "Amplitude": 0.1, "Octaves": 2,
             "BlendMode": "Add", "Seed": 44},
            {"Type": "Worley", "Frequency": 0.65, "Amplitude": 0.28, "Octaves": 1,
             "BlendMode": "Max", "Seed": 62},
        ],
    },
    "nullscape": {
        "desc": "Void realm — floating platforms, reality tears, static hum",
        "BaseHeight": 0.0,
        "HeightMultiplier": 1.15,
        "mods": {
            "HeightScale": 0.1, "Frequency": 0.28, "Persistence": 0.18, "Lacunarity": 1.45,
            "Detail": 0.12, "RidgeWeight": 0.65, "BillowWeight": 0.08, "RidgeSharpness": 2.8,
            "WarpStrength": 0.92, "WarpFrequency": 1.1,
            "CliffStrength": 0.72, "CliffFrequency": 0.48, "CliffSharpness": 0.03, "CliffTiers": 5,
            "CanyonDepth": 0.75, "CanyonFrequency": 0.85, "CanyonWidth": 0.22,
            "IslandScale": 0.55, "IslandDrop": 6.0,
            "TerracingStrength": 0.65, "TerracingSteps": 4,
            "MicroDetailFrequency": 5.5, "MicroDetailAmplitude": 0.2,
        },
        "layers": [
            {"Type": "Island", "Frequency": 0.42, "Amplitude": 1.0, "Octaves": 1,
             "BlendMode": "Max", "Seed": 0},
            {"Type": "Cliff", "Frequency": 0.5, "Amplitude": 0.85, "Octaves": 4,
             "BlendMode": "Max", "Seed": 77},
            {"Type": "Value", "Frequency": 4.8, "Amplitude": 0.32, "Octaves": 3,
             "BlendMode": "Add", "Seed": 99},
            {"Type": "Ridge", "Frequency": 2.2, "Amplitude": 0.35, "Octaves": 2,
             "BlendMode": "Screen", "Seed": 88},
        ],
    },
    "obsidian_expanse": {
        "desc": "Glass sea — mesa plateaus, lava fractures, crater bowls",
        "BaseHeight": 1.2,
        "HeightMultiplier": 1.18,
        "mods": {
            "HeightScale": 0.48, "Frequency": 1.25, "Persistence": 0.68, "Lacunarity": 2.65,
            "Detail": 0.88, "RidgeWeight": 0.72, "BillowWeight": 0.22, "RidgeSharpness": 2.0,
            "WarpStrength": 0.18, "WarpFrequency": 0.65,
            "ContinentalScale": 1.25,
            "CliffStrength": 0.68, "CliffFrequency": 0.5, "CliffSharpness": 0.05, "CliffTiers": 9,
            "CanyonDepth": 0.48, "CanyonFrequency": 0.55, "CanyonWidth": 0.32,
            "CraterFrequency": 1.15, "CraterDepth": 2.8, "CraterRimHeight": 1.1,
            "TerracingStrength": 0.58, "TerracingSteps": 10, "PlateauLevel": 0.68,
            "WetlandFlatten": 0.25,
            "OverhangStrength": 0.35, "OverhangFrequency": 1.4,
            "MicroDetailFrequency": 2.9, "MicroDetailAmplitude": 0.11,
        },
        "layers": [
            {"Type": "Billow", "Frequency": 0.35, "Amplitude": 0.28, "Octaves": 3,
             "BlendMode": "Add", "Seed": 14},
            {"Type": "Cliff", "Frequency": 0.45, "Amplitude": 0.9, "Octaves": 5,
             "BlendMode": "Max", "Seed": 26},
            {"Type": "Ridge", "Frequency": 2.2, "Amplitude": 0.42, "Octaves": 3,
             "BlendMode": "Max", "Seed": 38},
            {"Type": "Perlin", "Frequency": 0.72, "Amplitude": 0.18, "Octaves": 2,
             "BlendMode": "Add", "Seed": 52},
        ],
    },
    "shatterglass_desert": {
        "desc": "Glass desert — sharp crystal dunes, monolith spires, blinding flats",
        "BaseHeight": -0.8,
        "HeightMultiplier": 0.82,
        "mods": {
            "HeightScale": 0.16, "Frequency": 0.62, "Persistence": 0.28, "Lacunarity": 1.75,
            "Detail": 0.32, "RidgeWeight": 0.52, "BillowWeight": 0.28, "RidgeSharpness": 2.2,
            "WarpStrength": 0.38, "WarpFrequency": 0.48, "ErosionStrength": 0.35,
            "ContinentalScale": 0.38,
            "CliffStrength": 0.15, "CliffFrequency": 0.35, "CliffSharpness": 0.14, "CliffTiers": 3,
            "DuneFrequency": 2.2, "DuneAmplitude": 1.35, "DuneDirection": 115,
            "WetlandFlatten": 0.18,
            "MicroDetailFrequency": 5.2, "MicroDetailAmplitude": 0.2,
        },
        "layers": [
            {"Type": "Ridge", "Frequency": 1.85, "Amplitude": 0.38, "Octaves": 3,
             "Persistence": 0.42, "Lacunarity": 2.4, "BlendMode": "Max", "Seed": 16},
            {"Type": "Billow", "Frequency": 1.05, "Amplitude": 0.38, "Octaves": 3,
             "BlendMode": "Add", "Seed": 32},
            {"Type": "Worley", "Frequency": 0.62, "Amplitude": 0.55, "Octaves": 1,
             "BlendMode": "Max", "Seed": 48},
            {"Type": "Value", "Frequency": 5.5, "Amplitude": 0.14, "Octaves": 2,
             "BlendMode": "Screen", "Seed": 64},
        ],
    },
    "solaris_steppe": {
        "desc": "Golden steppe — rolling heat waves, dry creeks, scattered kopjes",
        "BaseHeight": 0.0,
        "HeightMultiplier": 0.95,
        "mods": {
            "HeightScale": 0.22, "Frequency": 0.88, "Persistence": 0.36, "Lacunarity": 1.95,
            "Detail": 0.48, "RidgeWeight": 0.15, "BillowWeight": 0.52, "RidgeSharpness": 1.2,
            "WarpStrength": 0.12, "WarpFrequency": 0.42, "ErosionStrength": 0.48,
            "ContinentalScale": 0.95,
            "CliffStrength": 0.12, "CliffFrequency": 0.28, "CliffSharpness": 0.2, "CliffTiers": 3,
            "CanyonDepth": 0.25, "CanyonFrequency": 0.42, "CanyonWidth": 0.48,
            "RollingHillsAmplitude": 0.55, "RollingHillsFrequency": 0.72,
            "SlopeErosionScale": 0.28,
            "MicroDetailFrequency": 3.8, "MicroDetailAmplitude": 0.14,
        },
        "layers": [
            {"Type": "Rolling", "Frequency": 0.68, "Amplitude": 0.55, "Octaves": 3,
             "BlendMode": "Add", "Seed": 42},
            {"Type": "Billow", "Frequency": 0.72, "Amplitude": 0.38, "Octaves": 3,
             "Persistence": 0.38, "Lacunarity": 2.1, "BlendMode": "Add", "Seed": 18},
            {"Type": "Perlin", "Frequency": 0.52, "Amplitude": 0.22, "Octaves": 2,
             "BlendMode": "Add", "Seed": 36},
            {"Type": "Canyon", "Frequency": 0.48, "Amplitude": -0.28, "Octaves": 3,
             "BlendMode": "Add", "Seed": 54},
            {"Type": "Worley", "Frequency": 0.42, "Amplitude": 0.22, "Octaves": 1,
             "BlendMode": "Max", "Seed": 72},
            {"Type": "Value", "Frequency": 4.2, "Amplitude": 0.09, "Octaves": 2,
             "BlendMode": "Screen", "Seed": 90},
        ],
    },
    "verdigris_expanse": {
        "desc": "Corroded badlands — acid pools, erosion gullies, metallic mounds",
        "BaseHeight": -0.2,
        "HeightMultiplier": 1.05,
        "mods": {
            "HeightScale": 0.38, "Frequency": 1.22, "Persistence": 0.52, "Lacunarity": 2.35,
            "Detail": 0.62, "RidgeWeight": 0.42, "BillowWeight": 0.32, "RidgeSharpness": 1.8,
            "WarpStrength": 0.28, "WarpFrequency": 0.55, "ErosionStrength": 0.68,
            "ValleyDepthBias": 0.58, "ContinentalScale": 0.88,
            "CliffStrength": 0.48, "CliffFrequency": 0.58, "CliffSharpness": 0.09, "CliffTiers": 5,
            "CanyonDepth": 0.68, "CanyonFrequency": 0.65, "CanyonWidth": 0.3,
            "PondDepth": 0.72, "PondFrequency": 2.0,
            "BasinStrength": 0.22,
            "MicroDetailFrequency": 3.0, "MicroDetailAmplitude": 0.15,
        },
        "layers": [
            {"Type": "Ridge", "Frequency": 1.45, "Amplitude": 0.38, "Octaves": 4,
             "Persistence": 0.48, "Lacunarity": 2.3, "BlendMode": "Add", "Seed": 10},
            {"Type": "Cliff", "Frequency": 0.55, "Amplitude": 0.48, "Octaves": 4,
             "BlendMode": "Add", "Seed": 22},
            {"Type": "Pond", "Frequency": 1.85, "Amplitude": 0.68, "Octaves": 1,
             "BlendMode": "Add", "Seed": 34},
            {"Type": "Canyon", "Frequency": 0.88, "Amplitude": -0.62, "Octaves": 4,
             "BlendMode": "Add", "Seed": 46},
            {"Type": "Billow", "Frequency": 0.62, "Amplitude": 0.25, "Octaves": 3,
             "BlendMode": "Add", "Seed": 58},
        ],
    },
}


def apply_recipe(data: dict, recipe: dict) -> None:
    data["BaseHeight"] = recipe["BaseHeight"]
    data["HeightMultiplier"] = recipe["HeightMultiplier"]
    mods = data["ProceduralData"]["NoiseModifiers"]
    mods.clear()
    mods.update(recipe["mods"])
    layers = []
    for layer in recipe["layers"]:
        entry = {"Enabled": True, **layer}
        entry.setdefault("Persistence", 0.5)
        entry.setdefault("Lacunarity", 2.0)
        entry.setdefault("Offset", 0.0)
        layers.append(entry)
    data["NoiseLayers"] = layers


def main() -> None:
    for path in sorted(glob.glob("/workspace/Veilborne.Core/assets/config/biomes/*.json")):
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        bid = data["Id"]
        if bid not in RECIPES:
            print(f"SKIP {bid}")
            continue
        apply_recipe(data, RECIPES[bid])
        with open(path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=4)
            f.write("\n")
        r = RECIPES[bid]
        print(f"OK {bid}: {r['desc']} ({len(r['layers'])} layers)")


if __name__ == "__main__":
    main()
