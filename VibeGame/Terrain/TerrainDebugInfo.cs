using System.Numerics;

namespace VibeGame.Terrain
{
    public record TerrainDebugInfo(
        int ChunkX, 
        int ChunkZ, 
        int LocalX,
        int LocalZ,
        int ChunkSize,
        float TileSize,
        string BiomeId,
        Vector3 WorldPos)
    {
    }
}
