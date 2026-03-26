using System.Text.Json;
using Veilborne.Biomes;
using Veilborne.Biomes.Environment;
using Veilborne.Terrain;

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

            if (path != null)
            {
                Config = JsonModelLoader.LoadFile<WorldConfig>(path);
                Seed = Config.WorldSeed != 0 ? Config.WorldSeed : Random.Shared.Next();
            }
            else
            {
                Config = new WorldConfig();
                Seed = Random.Shared.Next();
            }

            TerrainConfig = new TerrainRingConfig
            {
                EditableRadius = Config.EditableRadius,
                ReadOnlyRadius = Config.ReadOnlyRadius,
                LowLodRadius = Config.LowLodRadius,
                MaxEditable = Math.Max(1, Config.EditableRadius + 1),
                MaxReadOnly = Math.Max(2, Config.ReadOnlyRadius + 2),
                MaxLowLod = Math.Max(3, Config.LowLodRadius + 4),
            };

            NoiseConfig = Config.Noise ?? new MultiNoiseConfig();
            NoiseConfig.Seed = Seed;

            BiomeProviderConfig = Config.BiomeProvider ?? new BiomeProviderConfig();
            BiomeProviderConfig.Seed = Seed;
        }
    }
}
