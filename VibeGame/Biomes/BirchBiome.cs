using System.Numerics;
using VibeGame.Terrain;
using VibeGame.Biomes.Spawners;

namespace VibeGame.Biomes
{
    public class BirchBiome : IBiome
    {
        public string Id => "Birch";
        public ITreeSpawner TreeSpawner { get; }

        public BirchBiome(ITreeSpawner? spawner = null)
        {
            TreeSpawner = spawner ?? new BirchTreeSpawner();
        }

        // Example rule: lower elevations favor birch
        public bool Contains(Vector2 worldPos, ITerrainGenerator terrain)
        {
            float h = terrain.ComputeHeight(worldPos.X, worldPos.Y);
            return h < 6.0f; // simple threshold rule
        }
    }
}
