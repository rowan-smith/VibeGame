using System;
using System.Collections.Generic;
using Veilborne.Biomes;
using Veilborne.Biomes.Environment;

namespace Veilborne.Core
{
    public sealed class WorldConfig
    {
        public int WorldSeed { get; set; } = 0;
        public int EditableRadius { get; set; } = 3;
        public int ReadOnlyRadius { get; set; } = 6;
        public int LowLodRadius { get; set; } = 12;
        public int MaxActiveVoxelChunks { get; set; } = 128;
        public float DigRadius { get; set; } = 1.0f;
        public float DigStrength { get; set; } = 1.0f;
        public string DigFalloff { get; set; } = "Linear";
        public float DigMaxDepth { get; set; } = 2.2f;
        public float DigSmoothness { get; set; } = 0.28f;
        public int TerrainLoadQueueRadius { get; set; } = 4;
        public TerrainLayerConfig TerrainLayers { get; set; } = new();
        public Dictionary<string, BiomeMiningRule> BiomeMining { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public MultiNoiseConfig? Noise { get; set; }
        public BiomeProviderConfig? BiomeProvider { get; set; }
    }

    public sealed class TerrainLayerConfig
    {
        public string SurfaceTextureId { get; set; } = "brown_mud_leaves";
        public string SubsurfaceTextureId { get; set; } = "brown_mud";
        public string DeepTextureId { get; set; } = "rock_3";
        public float SubsurfaceDepth { get; set; } = 0.35f;
        public float DeepDepth { get; set; } = 1.20f;
    }

    public sealed class BiomeMiningRule
    {
        public string OreType { get; set; } = "coal";
        public float OreNoiseFrequency { get; set; } = 6.0f;
        public float OreThreshold { get; set; } = 0.72f;
        public float OreMinDepth { get; set; } = 0.35f;
        public float OreMaxDepth { get; set; } = 1.2f;
        public float OreSpawnChance { get; set; } = 0.08f;
    }
}
