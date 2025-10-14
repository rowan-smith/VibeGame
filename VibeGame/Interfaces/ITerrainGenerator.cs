using System.Numerics;

namespace VibeGame.Terrain
{
    public interface ITerrainGenerator
    {
        int TerrainSize { get; }
        float TileSize { get; }

        // Generate full heightmap
        float[,] GenerateHeights();

        // Generate a heightmap for a chunk at (chunkX, chunkZ)
        float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize);

        // Sample an existing heightmap
        float SampleHeight(float[,] heights, float worldX, float worldZ);

        // Compute base infinite height at arbitrary world position
        float ComputeHeight(float worldX, float worldZ);
    }
}
