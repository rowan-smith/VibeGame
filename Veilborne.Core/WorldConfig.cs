using Veilborne.Biomes;
using Veilborne.Biomes.Environment;

namespace Veilborne
{
    public sealed class WorldConfig
    {
        public int WorldSeed { get; set; } = 0;
        public int EditableRadius { get; set; } = 3;
        public int ReadOnlyRadius { get; set; } = 6;
        public int LowLodRadius { get; set; } = 12;
        public int MaxActiveVoxelChunks { get; set; } = 128;
        public DigConfig Dig { get; set; } = new();
        public int TerrainLoadQueueRadius { get; set; } = 4;
        public TerrainRuntimeConfig TerrainRuntime { get; set; } = new();
        public WorldObjectRenderConfig WorldObjectRender { get; set; } = new();
        public MultiNoiseConfig? Noise { get; set; }
        public BiomeProviderConfig? BiomeProvider { get; set; }
    }

    public sealed class DigConfig
    {
        public float Radius { get; set; } = 0.5f;
        public float Strength { get; set; } = 0.25f;
        public string Falloff { get; set; } = "Stepped";
        public float MaxDepth { get; set; } = 4.0f;
        public float Smoothness { get; set; } = 0.0f;
        public int MaxTerrainEditsPerFrame { get; set; } = 8;
        public bool SpawnParticles { get; set; } = true;
        public int ParticlesPerDig { get; set; } = 6;
        public float ParticleLifetime { get; set; } = 0.8f;
        /// <summary>Height is quantized to this step size for blocky mining feel. 0 = no quantization.</summary>
        public float BlockStepSize { get; set; } = 0.25f;
    }

    public sealed class TerrainRuntimeConfig
    {
        public int MinEditableRadius { get; set; } = 1;
        public int MaxEditableRadius { get; set; } = 4;
        public int MinReadOnlyRadius { get; set; } = 2;
        public int MaxReadOnlyRadius { get; set; } = 8;
        public int MinLowLodRadius { get; set; } = 3;
        public int MaxLowLodRadius { get; set; } = 16;
        public int ReadOnlyUpdateIntervalFrames { get; set; } = 4;
        public float SpeedScale { get; set; } = 0.15f;
        public float DensityPenalty { get; set; } = 1.0f;
        public float FpsTarget { get; set; } = 60f;
        public int MaxMeshBuildsPerFrame { get; set; } = 4;
        public int MaxEditableRebuildsPerFrame { get; set; } = 3;
        public int MaxReadOnlyChunkUpdatesPerFrame { get; set; } = 1;
        public int MaxLowLodChunkUpdatesPerFrame { get; set; } = 1;
        public int MaxReadOnlyInstallsPerFrame { get; set; } = 1;
        public int MaxLowLodInstallsPerFrame { get; set; } = 1;
        public int MaxReadOnlyConcurrentJobs { get; set; } = 8;
        public int MaxLowLodConcurrentJobs { get; set; } = 8;
        public float MaxTerrainDrawDistance { get; set; } = 1300f;
        /// <summary>Secondary texture pass distance as fraction of draw distance (0-1). Lower = fewer dual-pass chunks.</summary>
        public float SecondaryPassDistanceScale { get; set; } = 0.40f;
    }

    public sealed class WorldObjectRenderConfig
    {
        public float MaxDrawDistance { get; set; } = 90f;
        public float FoliageDrawDistanceMultiplier { get; set; } = 0.5f;
        public float MovingFoliageDrawDistanceMultiplier { get; set; } = 0.75f;
        public int MaxDetailedObjectsPerFrame { get; set; } = 600;
        public float MovingFrameBudgetScale { get; set; } = 0.70f;
        public int MaxNewModelLoadsPerFrame { get; set; } = 1;
        public int MaxNewModelLoadsWhileMoving { get; set; } = 0;
        public float FrustumCullingNearDistance { get; set; } = 1f;
        public float MaxReasonableModelDimension { get; set; } = 25f;
        public float AutoNormalizedTargetDimension { get; set; } = 6f;
        public float MaxBaseLiftMeters { get; set; } = 12f;
        public float MaxFinalWorldObjectDimension { get; set; } = 14f;
    }

    public sealed class TerrainLayerConfig
    {
        public string SurfaceTextureId { get; set; } = "brown_mud_leaves";
        public string SubsurfaceTextureId { get; set; } = "brown_mud";
        public string DeepTextureId { get; set; } = "rock_3";
        public string SlopeTextureId { get; set; } = "rock_3";
        public float SubsurfaceDepth { get; set; } = 0.35f;
        public float DeepDepth { get; set; } = 1.20f;
        public float DepthVariation { get; set; } = 0.15f;
        public float SlopeRockThreshold { get; set; } = 0.55f;
        public float SlopeBlendRange { get; set; } = 0.15f;

        /// <summary>Noise-driven variation of the surface texture weight (0 = uniform, 1 = very patchy).</summary>
        public float TopNoiseScale { get; set; } = 0f;

        /// <summary>Frequency of depth transition noise. Higher values create more granular subsurface patterning.</summary>
        public float DepthNoiseFrequency { get; set; } = 1f;

        /// <summary>Additional texture layers with depth/slope rules (optional, for multi-texture biomes).</summary>
        public List<TerrainSubLayer>? SubLayers { get; set; }
    }

    /// <summary>
    /// An additional terrain texture sub-layer with depth, slope, and noise-driven blending rules.
    /// Multiple sub-layers let biomes have patchy grass, scattered pebbles, etc.
    /// </summary>
    public sealed class TerrainSubLayer
    {
        public string TextureId { get; set; } = string.Empty;
        public float DepthMin { get; set; } = 0f;
        public float DepthMax { get; set; } = 0.5f;
        public float SlopeMin { get; set; } = 0f;
        public float SlopeMax { get; set; } = 1f;
        public float BlendStrength { get; set; } = 0.5f;
        /// <summary>Noise frequency for this sub-layer's spatial distribution.</summary>
        public float NoiseFrequency { get; set; } = 0.1f;
        /// <summary>Noise threshold below which this layer doesn't appear (creates patches).</summary>
        public float NoiseThreshold { get; set; } = 0.3f;
        /// <summary>Per-cell random depth variation for organic transitions.</summary>
        public float DepthVariation { get; set; } = 0.05f;
    }

}
