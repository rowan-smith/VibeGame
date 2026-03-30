using Veilborne.Core.Biomes;
using Veilborne.Core.Biomes.Environment;
using Veilborne.Core.Terrain;

namespace Veilborne.Core
{
    public interface IWorldConfigService
    {
        int Seed { get; }
        WorldConfig Config { get; }
        TerrainRingConfig TerrainConfig { get; }
        MultiNoiseConfig NoiseConfig { get; }
        BiomeProviderConfig BiomeProviderConfig { get; }
    }

    public class WorldConfigService : IWorldConfigService
    {
        public int Seed { get; private set; }
        public WorldConfig Config { get; private set; }
        public TerrainRingConfig TerrainConfig { get; private set; }
        public MultiNoiseConfig NoiseConfig { get; private set; }
        public BiomeProviderConfig BiomeProviderConfig { get; private set; }

        public WorldConfigService()
        {
            Initialize();
        }

        private void Initialize()
        {
            var baseDir = AppContext.BaseDirectory;
            var coreConfigPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "Veilborne.Core", "assets", "config", "world.json"));
            var path1 = Path.Combine(baseDir, "assets", "config", "world.json");
            var path2 = Path.Combine(baseDir, "assets", "config", "terrain", "world.json");
            string? path = File.Exists(coreConfigPath)
                ? coreConfigPath
                : (File.Exists(path1) ? path1 : (File.Exists(path2) ? path2 : null));

            if (path == null)
                throw new FileNotFoundException(
                    $"world.json not found. Searched: {coreConfigPath}, {path1}, {path2}");

            Config = JsonModelLoader.LoadFile<WorldConfig>(path);
            Seed = Config.WorldSeed != 0 ? Config.WorldSeed : Random.Shared.Next();

            if (Config.Dig == null)
                throw new InvalidOperationException("world.json is missing required 'Dig' section.");
            if (Config.Noise == null)
                throw new InvalidOperationException("world.json is missing required 'Noise' section.");
            if (Config.BiomeProvider == null)
                throw new InvalidOperationException("world.json is missing required 'BiomeProvider' section.");

            TerrainConfig = new TerrainRingConfig
            {
                EditableRadius = Config.EditableRadius,
                ReadOnlyRadius = Config.ReadOnlyRadius,
                LowLodRadius = Config.LowLodRadius,
                MinEditable = Math.Max(1, Config.TerrainRuntime.MinEditableRadius),
                MaxEditable = Math.Max(1, Config.TerrainRuntime.MaxEditableRadius),
                MinReadOnly = Math.Max(2, Config.TerrainRuntime.MinReadOnlyRadius),
                MaxReadOnly = Math.Max(2, Config.TerrainRuntime.MaxReadOnlyRadius),
                MinLowLod = Math.Max(3, Config.TerrainRuntime.MinLowLodRadius),
                MaxLowLod = Math.Max(3, Config.TerrainRuntime.MaxLowLodRadius),
                ReadOnlyUpdateInterval = Math.Max(1, Config.TerrainRuntime.ReadOnlyUpdateIntervalFrames),
                SpeedScale = MathF.Max(0f, Config.TerrainRuntime.SpeedScale),
                DensityPenalty = MathF.Max(0f, Config.TerrainRuntime.DensityPenalty),
                FpsTarget = MathF.Max(15f, Config.TerrainRuntime.FpsTarget),
                MaxMeshBuildsPerFrame = Math.Max(1, Config.TerrainRuntime.MaxMeshBuildsPerFrame),
                MaxEditableRebuildsPerFrame = Math.Max(1, Config.TerrainRuntime.MaxEditableRebuildsPerFrame),
                MaxReadOnlyChunkUpdatesPerFrame = Math.Max(1, Config.TerrainRuntime.MaxReadOnlyChunkUpdatesPerFrame),
                MaxLowLodChunkUpdatesPerFrame = Math.Max(1, Config.TerrainRuntime.MaxLowLodChunkUpdatesPerFrame),
                MaxReadOnlyInstallsPerFrame = Math.Max(1, Config.TerrainRuntime.MaxReadOnlyInstallsPerFrame),
                MaxLowLodInstallsPerFrame = Math.Max(1, Config.TerrainRuntime.MaxLowLodInstallsPerFrame),
                MaxReadOnlyConcurrentJobs = Math.Max(1, Config.TerrainRuntime.MaxReadOnlyConcurrentJobs),
                MaxLowLodConcurrentJobs = Math.Max(1, Config.TerrainRuntime.MaxLowLodConcurrentJobs),
            };

            if (TerrainConfig.MaxEditable < TerrainConfig.MinEditable)
                TerrainConfig.MaxEditable = TerrainConfig.MinEditable;
            if (TerrainConfig.MaxReadOnly < TerrainConfig.MinReadOnly)
                TerrainConfig.MaxReadOnly = TerrainConfig.MinReadOnly;
            if (TerrainConfig.MaxLowLod < TerrainConfig.MinLowLod)
                TerrainConfig.MaxLowLod = TerrainConfig.MinLowLod;

            NoiseConfig = Config.Noise!;
            NoiseConfig.Seed = Seed;

            BiomeProviderConfig = Config.BiomeProvider!;
            BiomeProviderConfig.Seed = Seed;
        }
    }
}
