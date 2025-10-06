using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    // Represents a biome definition that can provide a world object spawner and decide membership.
    public interface IBiome
    {
        string Id { get; }
        BiomeData Data { get; }
        // Returns true if the given world position belongs to this biome.
        bool Contains(Vector2 worldPos, ITerrainGenerator terrain);
        VibeGame.Objects.IWorldObjectSpawner ObjectSpawner { get; }
    }
}
