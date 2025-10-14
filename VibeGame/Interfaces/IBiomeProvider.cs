using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    public interface IBiomeProvider
    {
        IBiome GetBiomeAt(Vector2 worldPos, ITerrainGenerator terrain);
    }
}
