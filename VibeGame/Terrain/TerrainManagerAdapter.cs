using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Core
{
    /// <summary>
    /// Wraps the existing TerrainManager into an ITerrainGenerator for ObjectSpawner.
    /// </summary>
    public class TerrainManagerAdapter : ITerrainGenerator
    {
        private readonly TerrainManager _terrain;

        public TerrainManagerAdapter(TerrainManager terrain)
        {
            _terrain = terrain;
        }

        public int TerrainSize => _terrain.ChunkSize; // or a fixed constant if needed
        public float TileSize => _terrain.TileSize;

        public float ComputeHeight(float worldX, float worldZ)
        {
            return _terrain.SampleHeight(worldX, worldZ);
        }

        public float[,] GenerateHeights()
        {
            // Simplified: generate a single chunk's heights as a demo
            var size = TerrainSize;
            float[,] heights = new float[size, size];
            for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                float wx = x * TileSize;
                float wz = z * TileSize;
                heights[x, z] = _terrain.SampleHeight(wx, wz);
            }
            return heights;
        }

        public float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize)
        {
            float[,] heights = new float[chunkSize, chunkSize];
            float chunkWorld = (chunkSize - 1) * TileSize;
            float originX = chunkX * chunkWorld;
            float originZ = chunkZ * chunkWorld;

            for (int z = 0; z < chunkSize; z++)
            for (int x = 0; x < chunkSize; x++)
                heights[x, z] = _terrain.SampleHeight(originX + x * TileSize, originZ + z * TileSize);

            return heights;
        }

        public float SampleHeight(float[,] heights, float worldX, float worldZ)
        {
            // Optional: fallback to nearest cell
            int size = heights.GetLength(0);
            float gx = worldX / TileSize;
            float gz = worldZ / TileSize;

            int x0 = Math.Clamp((int)MathF.Floor(gx), 0, size - 1);
            int z0 = Math.Clamp((int)MathF.Floor(gz), 0, size - 1);

            return heights[x0, z0];
        }
    }
}
