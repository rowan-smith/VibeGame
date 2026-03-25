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
            var path1 = Path.Combine(baseDir, "assets", "config", "world.json");
            var path2 = Path.Combine(baseDir, "assets", "config", "terrain", "world.json");
            string? path = File.Exists(path1) ? path1 : (File.Exists(path2) ? path2 : null);

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
            };

            NoiseConfig = new MultiNoiseConfig { Seed = Seed };
            BiomeProviderConfig = new BiomeProviderConfig { Seed = Seed };
        }
    }
}
