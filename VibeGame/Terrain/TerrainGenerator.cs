using VibeGame.Biomes.Environment;

namespace VibeGame.Terrain
{
    public class TerrainGenerator : ITerrainGenerator
    {
        public int TerrainSize { get; } = 120;
        public float TileSize { get; } = 1.5f;
        private const float TerrainAmplitude = 6.0f;

        public TerrainGenerator(MultiNoiseConfig? cfg = null)
        {
            int seed = cfg?.Seed ?? 1337;
            // Initialize noise sources here
        }

        public float ComputeHeight(float worldX, float worldZ)
        {
            // More mountainous terrain using simple ridged multi-octave functions
            float nx = worldX * 0.005f;
            float nz = worldZ * 0.005f;

            float Ridge(float x, float z)
            {
                // A cheap ridge-like function based on periodic sines
                float s = MathF.Sin(x) * MathF.Cos(z);
                return 1f - MathF.Abs(s);
            }

            float h = 0f;
            h += Ridge(nx * 1.1f, nz * 1.1f) * 9.0f;
            h += Ridge(nx * 2.2f + 12.34f, nz * 2.0f + 45.67f) * 5.5f;
            h += Ridge(nx * 4.1f + 78.9f, nz * 3.8f + 12.3f) * 3.0f;

            // Broad undulation for large-scale valleys
            h += (MathF.Sin(worldX * 0.01f) + MathF.Cos(worldZ * 0.008f)) * 2.0f;

            // Raise baseline to keep above zero
            h += 2.5f;

            return h;
        }

        public float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize)
        {
            // Include shared boundary vertices so adjacent chunks stitch without gaps.
            // Size is (chunkSize + 1) so mesh covers exactly chunkSize * TileSize in world units.
            int size = chunkSize + 1;
            float[,] heights = new float[size, size];

            float chunkWorld = chunkSize * TileSize;
            float originX = chunkX * chunkWorld;
            float originZ = chunkZ * chunkWorld;

            for (int z = 0; z <= chunkSize; z++)
            {
                for (int x = 0; x <= chunkSize; x++)
                {
                    heights[x, z] = ComputeHeight(originX + x * TileSize, originZ + z * TileSize);
                }
            }

            return heights;
        }

        public float[,] GenerateHeights()
        {
            float[,] heights = new float[TerrainSize, TerrainSize];
            int half = TerrainSize / 2;
            for (int z = 0; z < TerrainSize; z++)
            for (int x = 0; x < TerrainSize; x++)
                heights[x, z] = ComputeHeight((x - half) * TileSize, (z - half) * TileSize);
            return heights;
        }

        public float SampleHeight(float[,] heights, float worldX, float worldZ)
        {
            int half = TerrainSize / 2;
            float gx = worldX / TileSize + half;
            float gz = worldZ / TileSize + half;

            int x0 = Math.Clamp((int)MathF.Floor(gx), 0, TerrainSize - 1);
            int z0 = Math.Clamp((int)MathF.Floor(gz), 0, TerrainSize - 1);
            return heights[x0, z0];
        }
    }
}
