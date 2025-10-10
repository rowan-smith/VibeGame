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
            // TerrainManager expects a Vector3
            return _terrain.SampleHeight(new Vector3(worldX, 0, worldZ));
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
                heights[x, z] = _terrain.SampleHeight(new Vector3(wx, 0, wz));
            }
            return heights;
        }

        /// <summary>
        /// Generate heights for a specific chunk at chunk coordinates.
        /// </summary>
        public float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize)
        {
            // Include border vertices to avoid seams between chunks
            var heights = new float[chunkSize + 1, chunkSize + 1];
            float chunkWorld = chunkSize * TileSize;

            float originX = chunkX * chunkWorld;
            float originZ = chunkZ * chunkWorld;

            for (int z = 0; z <= chunkSize; z++)
            for (int x = 0; x <= chunkSize; x++)
            {
                float worldX = originX + x * TileSize;
                float worldZ = originZ + z * TileSize;
                heights[x, z] = ComputeHeight(worldX, worldZ);
            }

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
