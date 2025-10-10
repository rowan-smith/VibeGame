using System.Numerics;

namespace VibeGame.Terrain
{
    public class ReadOnlyTerrainAdapter : ITerrainGenerator
    {
        private readonly ReadOnlyTerrainService _readOnly;

        public ReadOnlyTerrainAdapter(ReadOnlyTerrainService readOnly)
        {
            _readOnly = readOnly;
        }

        public float TileSize => _readOnly.TileSize;
        public int TerrainSize => _readOnly.ChunkSize;

        public float ComputeHeight(float worldX, float worldZ)
        {
            return _readOnly.SampleHeight(worldX, worldZ);
        }

        public float[,] GenerateHeights()
        {
            int size = TerrainSize;
            float[,] heights = new float[size, size];
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
                heights[x, z] = _readOnly.SampleHeight(x * TileSize, z * TileSize);
            return heights;
        }

        public float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize)
        {
            float[,] heights = new float[chunkSize, chunkSize];
            float originX = chunkX * chunkSize * TileSize;
            float originZ = chunkZ * chunkSize * TileSize;
            for (int z = 0; z < chunkSize; z++)
            for (int x = 0; x < chunkSize; x++)
                heights[x, z] = _readOnly.SampleHeight(originX + x * TileSize, originZ + z * TileSize);
            return heights;
        }

        public float SampleHeight(float[,] heights, float worldX, float worldZ)
        {
            int size = heights.GetLength(0);
            float gx = worldX / TileSize;
            float gz = worldZ / TileSize;

            int x0 = Math.Clamp((int)MathF.Floor(gx), 0, size - 1);
            int z0 = Math.Clamp((int)MathF.Floor(gz), 0, size - 1);

            return heights[x0, z0];
        }
    }
}
