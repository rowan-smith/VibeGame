using System.Numerics;

namespace VibeGame
{
    public interface ITerrainGenerator
    {
        int TerrainSize { get; }
        float TileSize { get; }
        float[,] GenerateHeights();
        float SampleHeight(float[,] heights, float worldX, float worldZ);
    }
}
