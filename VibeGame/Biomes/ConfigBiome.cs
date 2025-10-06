using System.Numerics;
using VibeGame.Biomes.Spawners;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    /// Generic biome created from configuration data.
    public sealed class ConfigBiome : IBiome
    {
        public string Id { get; }
        public BiomeData Data { get; }
        public VibeGame.Objects.IWorldObjectSpawner ObjectSpawner { get; }

        public ConfigBiome(string id, BiomeData data, VibeGame.Objects.IWorldObjectSpawner objectSpawner)
        {
            Id = id;
            Data = data;
            ObjectSpawner = objectSpawner;
        }

        // Membership is decided by the MultiNoiseBiomeProvider; individual biomes return true.
        public bool Contains(Vector2 worldPos, ITerrainGenerator terrain) => true;
    }
}
