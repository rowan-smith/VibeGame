using Veilborne.Core.Biomes.Environment;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.Terrain
{
    public class TerrainGenerator : ITerrainGenerator
    {
        public int TerrainSize { get; } = 120;
        public float TileSize { get; } = 1.5f;
        private readonly INoiseSource _macroNoise;
        private readonly INoiseSource _ridgeNoise;
        private readonly INoiseSource _detailNoise;
        private readonly INoiseSource _microNoise;

        public TerrainGenerator(MultiNoiseConfig? cfg = null)
        {
            int seed = cfg?.Seed ?? 1337;
            _macroNoise = new FastNoiseLiteSource(
                seed + 11,
                FastNoiseLite.NoiseType.OpenSimplex2S,
                0.0018f,
                octaves: 4,
                lacunarity: 2.0f,
                gain: 0.52f);

            _ridgeNoise = new FastNoiseLiteSource(
                seed + 23,
                FastNoiseLite.NoiseType.OpenSimplex2,
                0.0045f,
                octaves: 4,
                lacunarity: 2.15f,
                gain: 0.5f);

            _detailNoise = new FastNoiseLiteSource(
                seed + 37,
                FastNoiseLite.NoiseType.OpenSimplex2S,
                0.011f,
                octaves: 3,
                lacunarity: 2.2f,
                gain: 0.48f);

            _microNoise = new FastNoiseLiteSource(
                seed + 59,
                FastNoiseLite.NoiseType.OpenSimplex2,
                0.024f,
                octaves: 2,
                lacunarity: 2.0f,
                gain: 0.45f);
        }

        public float ComputeHeight(float worldX, float worldZ, float detailLevel = 1f)
        {
            float macro = _macroNoise.GetValue3D(worldX, 0f, worldZ);   // [-1,1]
            float ridgeRaw = _ridgeNoise.GetValue3D(worldX, 0f, worldZ); // [-1,1]

            // Skip medium/fine details if low-detail requested
            float detail = detailLevel > 0.5f ? _detailNoise.GetValue3D(worldX, 0f, worldZ) : 0f;
            float micro = detailLevel > 0.8f ? _microNoise.GetValue3D(worldX, 0f, worldZ) : 0f;

            // Ridged transform with sharpening
            float ridge = 1f - MathF.Abs(ridgeRaw);
            ridge = MathF.Pow(Math.Clamp(ridge, 0f, 1f), 1.35f);

            // Keep a stable baseline
            float h = 2.2f;
            h += (macro * 0.5f + 0.5f) * 8.5f; // large forms
            h += ridge * 8.0f;                 // mountain ridges
            h += detail * 2.8f;                // medium breakup
            h += micro * 1.1f;                 // fine noise

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
