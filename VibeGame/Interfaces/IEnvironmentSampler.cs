using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Environment
{
    public interface IEnvironmentSampler
    {
        EnvironmentSample Sample(Vector2 worldPos, ITerrainGenerator terrain);
    }
}
