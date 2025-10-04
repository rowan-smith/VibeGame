using System.Numerics;

namespace VibeGame
{
    public interface ITerrainGenerator
    {
        int TerrainSize { get; }
        float TileSize { get; }
        float[,] GenerateHeights();
        float SampleHeight(float[,] heights, float worldX, float worldZ);
        // New: compute base infinite height at arbitrary world position (no island falloff)
        float ComputeHeight(float worldX, float worldZ);
        // New: generate a heightmap for a chunk located at (chunkX,chunkZ) in chunk grid
        float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize);
    }
}
