using System.Numerics;

namespace Veilborne.Terrain
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
