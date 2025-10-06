using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    // Selects which biome applies at a given world position.
    public interface IBiomeProvider
    {
        IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain);
    }
}
