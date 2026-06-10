using Veilborne.Biomes;
using Veilborne.Biomes.Environment;
using Veilborne.Terrain;

namespace Veilborne.Web.WebImpl;

/// <summary>
/// WASM-friendly world config loaded from pre-fetched JSON instead of disk paths.
/// </summary>
public sealed class WebWorldConfigService : IWorldConfigService
{
    public int Seed { get; }
    public WorldConfig Config { get; }
    public TerrainRingConfig TerrainConfig { get; }
    public MultiNoiseConfig NoiseConfig { get; }
    public BiomeProviderConfig BiomeProviderConfig { get; }

    public WebWorldConfigService(WorldConfig config)
    {
        Config = config;
        Seed = config.WorldSeed != 0 ? config.WorldSeed : Random.Shared.Next();

        if (config.Dig == null)
            throw new InvalidOperationException("world.json is missing required 'Dig' section.");
        if (config.Noise == null)
            throw new InvalidOperationException("world.json is missing required 'Noise' section.");
        if (config.BiomeProvider == null)
            throw new InvalidOperationException("world.json is missing required 'BiomeProvider' section.");

        TerrainConfig = new TerrainRingConfig
        {
            EditableRadius = config.EditableRadius,
            ReadOnlyRadius = config.ReadOnlyRadius,
            LowLodRadius = config.LowLodRadius,
            MinEditable = Math.Max(1, config.TerrainRuntime.MinEditableRadius),
            MaxEditable = Math.Max(1, config.TerrainRuntime.MaxEditableRadius),
            MinReadOnly = Math.Max(2, config.TerrainRuntime.MinReadOnlyRadius),
            MaxReadOnly = Math.Max(2, config.TerrainRuntime.MaxReadOnlyRadius),
            MinLowLod = Math.Max(3, config.TerrainRuntime.MinLowLodRadius),
            MaxLowLod = Math.Max(3, config.TerrainRuntime.MaxLowLodRadius),
            ReadOnlyUpdateInterval = Math.Max(1, config.TerrainRuntime.ReadOnlyUpdateIntervalFrames),
            SpeedScale = MathF.Max(0f, config.TerrainRuntime.SpeedScale),
            DensityPenalty = MathF.Max(0f, config.TerrainRuntime.DensityPenalty),
            FpsTarget = MathF.Max(15f, config.TerrainRuntime.FpsTarget),
            MaxMeshBuildsPerFrame = Math.Max(1, config.TerrainRuntime.MaxMeshBuildsPerFrame),
            MaxEditableRebuildsPerFrame = Math.Max(1, config.TerrainRuntime.MaxEditableRebuildsPerFrame),
            MaxReadOnlyChunkUpdatesPerFrame = Math.Max(1, config.TerrainRuntime.MaxReadOnlyChunkUpdatesPerFrame),
            MaxLowLodChunkUpdatesPerFrame = Math.Max(1, config.TerrainRuntime.MaxLowLodChunkUpdatesPerFrame),
            MaxReadOnlyInstallsPerFrame = Math.Max(1, config.TerrainRuntime.MaxReadOnlyInstallsPerFrame),
            MaxLowLodInstallsPerFrame = Math.Max(1, config.TerrainRuntime.MaxLowLodInstallsPerFrame),
            MaxReadOnlyConcurrentJobs = Math.Max(1, config.TerrainRuntime.MaxReadOnlyConcurrentJobs),
            MaxLowLodConcurrentJobs = Math.Max(1, config.TerrainRuntime.MaxLowLodConcurrentJobs),
        };

        if (TerrainConfig.MaxEditable < TerrainConfig.MinEditable)
            TerrainConfig.MaxEditable = TerrainConfig.MinEditable;
        if (TerrainConfig.MaxReadOnly < TerrainConfig.MinReadOnly)
            TerrainConfig.MaxReadOnly = TerrainConfig.MinReadOnly;
        if (TerrainConfig.MaxLowLod < TerrainConfig.MinLowLod)
            TerrainConfig.MaxLowLod = TerrainConfig.MinLowLod;

        NoiseConfig = config.Noise!;
        NoiseConfig.Seed = Seed;

        BiomeProviderConfig = config.BiomeProvider!;
        BiomeProviderConfig.Seed = Seed;
    }
}
