using LibNoise;
using LibNoise.Filter;
using LibNoise.Primitive;

namespace VibeGame.Terrain
{
    public class TerrainGenerator : ITerrainGenerator
    {
        // Expose to engine via interface
        public int TerrainSize { get; } = 120;
        public float TileSize { get; } = 1.5f;

        // Noise params (tuned for smoother rolling terrain)
        private const float TerrainScale = 0.03f;
        private const float TerrainAmplitude = 3.8f;
        private const int TerrainOctaves = 4;
        private const float TerrainLacunarity = 2.0f;
        private const float TerrainGain = 0.55f;

        // LibNoise modules
        private readonly SumFractal _warpA;
        private readonly SumFractal _warpB;
        private readonly SumFractal _baseFbm;
        private readonly RidgedMultiFractal _ridged;

        public TerrainGenerator()
        {
            int seed = 1337;
            // Domain warp sources (lower frequency, few octaves)
            _warpA = new SumFractal();
            _warpA.Primitive3D = new ImprovedPerlin(seed + 1, NoiseQuality.Standard);
            _warpA.Frequency = TerrainScale * 0.5f;
            _warpA.Lacunarity = 2.0f;
            _warpA.Gain = 0.5f;
            _warpA.OctaveCount = 3f;

            _warpB = new SumFractal();
            _warpB.Primitive3D = new ImprovedPerlin(seed + 2, NoiseQuality.Standard);
            _warpB.Frequency = TerrainScale * 0.5f;
            _warpB.Lacunarity = 2.0f;
            _warpB.Gain = 0.5f;
            _warpB.OctaveCount = 3f;

            // Base terrain FBM
            _baseFbm = new SumFractal();
            _baseFbm.Primitive3D = new ImprovedPerlin(seed + 3, NoiseQuality.Standard);
            _baseFbm.Frequency = TerrainScale;
            _baseFbm.Lacunarity = TerrainLacunarity;
            _baseFbm.Gain = TerrainGain;
            _baseFbm.OctaveCount = TerrainOctaves;

            // Ridged contribution
            _ridged = new RidgedMultiFractal();
            _ridged.Primitive3D = new ImprovedPerlin(seed + 4, NoiseQuality.Standard);
            _ridged.Frequency = TerrainScale * 0.6f;
            _ridged.Lacunarity = 2.0f;
            _ridged.OctaveCount = 4f;
        }

        // Base infinite height function (no island falloff)
        public float ComputeHeight(float worldX, float worldZ)
        {
            // Domain warp to break up grid-aligned patterns
            float warp = 0.35f;
            float wa = _warpA.GetValue(worldX * TerrainScale * 0.5f + 100.0f, 0.0f, worldZ * TerrainScale * 0.5f + 100.0f);
            float wb = _warpB.GetValue(worldX * TerrainScale * 0.5f - 100.0f, 0.0f, worldZ * TerrainScale * 0.5f - 100.0f);
            float wxWarp = worldX + ((wa + 1.0f) * 0.5f - 0.5f) * warp * 20f;
            float wzWarp = worldZ + ((wb + 1.0f) * 0.5f - 0.5f) * warp * 20f;

            float baseVal = _baseFbm.GetValue(wxWarp * TerrainScale, 0.0f, wzWarp * TerrainScale);
            float ridgedVal = _ridged.GetValue(wxWarp * TerrainScale * 0.6f, 0.0f, wzWarp * TerrainScale * 0.6f);
            float base01 = (baseVal + 1.0f) * 0.5f;
            float ridged01 = (ridgedVal + 1.0f) * 0.5f;
            float h01 = base01 * 0.7f + ridged01 * 0.6f;
            return h01 * TerrainAmplitude;
        }

        public float[,] GenerateHeightsForChunk(int chunkX, int chunkZ, int chunkSize)
        {
            float[,] heights = new float[chunkSize, chunkSize];
            // Important: Chunk world stride must match rendered tile coverage (chunkSize - 1) tiles
            float chunkWorldSize = (chunkSize - 1) * TileSize;
            float originX = chunkX * chunkWorldSize;
            float originZ = chunkZ * chunkWorldSize;
            for (int z = 0; z < chunkSize; z++)
            {
                for (int x = 0; x < chunkSize; x++)
                {
                    float wx = originX + x * TileSize;
                    float wz = originZ + z * TileSize;
                    heights[x, z] = ComputeHeight(wx, wz);
                }
            }
            return heights;
        }

        public float[,] GenerateHeights()
        {
            float[,] heights = new float[TerrainSize, TerrainSize];
            int half = TerrainSize / 2;

            for (int z = 0; z < TerrainSize; z++)
            {
                for (int x = 0; x < TerrainSize; x++)
                {
                    float wx = (x - half) * TileSize;
                    float wz = (z - half) * TileSize;

                    float baseH = ComputeHeight(wx, wz);

                    // Edge falloff island for the finite preview map
                    float nx = (x / (float)(TerrainSize - 1)) * 2f - 1f;
                    float nz = (z / (float)(TerrainSize - 1)) * 2f - 1f;
                    float r = MathF.Sqrt(nx * nx + nz * nz);
                    float falloff = Math.Clamp(1f - MathF.Pow(MathF.Max(0f, r - 0.6f) / 0.4f, 2f), 0f, 1f);

                    heights[x, z] = baseH * falloff;
                }
            }

            // Light smoothing pass
            float[,] smooth = new float[TerrainSize, TerrainSize];
            for (int z = 1; z < TerrainSize - 1; z++)
            {
                for (int x = 1; x < TerrainSize - 1; x++)
                {
                    float sum = heights[x, z] * 4f + heights[x - 1, z] + heights[x + 1, z] + heights[x, z - 1] + heights[x, z + 1];
                    smooth[x, z] = sum / 8f;
                }
            }
            for (int z = 1; z < TerrainSize - 1; z++)
                for (int x = 1; x < TerrainSize - 1; x++)
                    heights[x, z] = smooth[x, z];

            return heights;
        }

        public float SampleHeight(float[,] heights, float worldX, float worldZ)
        {
            int half = TerrainSize / 2;
            float gx = worldX / TileSize + half;
            float gz = worldZ / TileSize + half;

            int x0 = (int)MathF.Floor(gx);
            int z0 = (int)MathF.Floor(gz);
            int x1 = x0 + 1;
            int z1 = z0 + 1;

            // clamp to grid
            x0 = Math.Clamp(x0, 0, TerrainSize - 1);
            z0 = Math.Clamp(z0, 0, TerrainSize - 1);
            x1 = Math.Clamp(x1, 0, TerrainSize - 1);
            z1 = Math.Clamp(z1, 0, TerrainSize - 1);

            float tx = Math.Clamp(gx - x0, 0, 1);
            float tz = Math.Clamp(gz - z0, 0, 1);

            float h00 = heights[x0, z0];
            float h10 = heights[x1, z0];
            float h01 = heights[x0, z1];
            float h11 = heights[x1, z1];

            float hx0 = h00 + (h10 - h00) * tx;
            float hx1 = h01 + (h11 - h01) * tz;
            return hx0 + (hx1 - hx0) * tz;
        }
    }
}
