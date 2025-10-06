using System.Numerics;
using VibeGame.Terrain;
using VibeGame.Biomes.Spawners;

namespace VibeGame.Biomes
{
    public class PineBiome : IBiome
    {
        public string Id => "Pine";
        public ITreeSpawner TreeSpawner { get; }

        public PineBiome(ITreeSpawner? spawner = null)
        {
            TreeSpawner = spawner ?? new PineTreeSpawner();
        }

        // Example rule: higher elevations favor pine
        public bool Contains(Vector2 worldPos, ITerrainGenerator terrain)
        {
            float h = terrain.ComputeHeight(worldPos.X, worldPos.Y);
            return h >= 6.0f; // complement of BirchBiome rule
        }
    }
}
