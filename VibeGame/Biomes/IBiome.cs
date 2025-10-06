using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    // Represents a biome definition that can provide a tree spawner and decide membership.
    public interface IBiome
    {
        string Id { get; }
        // Returns true if the given world position belongs to this biome.
        bool Contains(Vector2 worldPos, ITerrainGenerator terrain);
        ITreeSpawner TreeSpawner { get; }
    }
}
