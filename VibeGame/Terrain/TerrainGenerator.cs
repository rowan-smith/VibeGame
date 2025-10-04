namespace VibeGame.Terrain
{
    public class TerrainGenerator : ITerrainGenerator
    {
        // Expose to engine via interface
        public int TerrainSize { get; } = 120;
        public float TileSize { get; } = 1.5f;

        // Noise params (tuned for richer layered terrain)
        private const float TerrainScale = 0.03f;       // mid frequency
        private const float MacroScale = 0.008f;        // large hills/mountains
        private const float DetailScale = 0.08f;        // small undulations
        private const float TerrainAmplitude = 6.0f;    // overall amplitude increased
        private const int TerrainOctaves = 4;
        private const float TerrainLacunarity = 2.0f;
        private const float TerrainGain = 0.55f;

        // Noise sources via FastNoiseLite
        private readonly INoiseSource _warpA;
        private readonly INoiseSource _warpB;
        private readonly INoiseSource _baseFbm;
        private readonly INoiseSource _macroFbm;
        private readonly INoiseSource _detailFbm;
        private readonly INoiseSource _ridged;

        public TerrainGenerator(
            FastNoiseLite.NoiseType baseType = FastNoiseLite.NoiseType.OpenSimplex2,
            FastNoiseLite.NoiseType macroType = FastNoiseLite.NoiseType.OpenSimplex2,
            FastNoiseLite.NoiseType detailType = FastNoiseLite.NoiseType.OpenSimplex2S,
            FastNoiseLite.NoiseType ridgedType = FastNoiseLite.NoiseType.OpenSimplex2S)
        {
            int seed = 1337;
            // Domain warp sources (lower frequency, few octaves)
            _warpA = new FastNoiseLiteSource(seed + 1, FastNoiseLite.NoiseType.OpenSimplex2, TerrainScale * 0.5f, 3, 2.0f, 0.5f);
            _warpB = new FastNoiseLiteSource(seed + 2, FastNoiseLite.NoiseType.OpenSimplex2, TerrainScale * 0.5f, 3, 2.0f, 0.5f);

            // Base terrain FBM
            _baseFbm = new FastNoiseLiteSource(seed + 3, baseType, TerrainScale, TerrainOctaves, TerrainLacunarity, TerrainGain);

            // Macro terrain (broad hills)
            _macroFbm = new FastNoiseLiteSource(seed + 10, macroType, MacroScale, 3, 2.0f, 0.5f);

            // Detail terrain (small ripples)
            _detailFbm = new FastNoiseLiteSource(seed + 11, detailType, DetailScale, 2, 2.2f, 0.55f);

            // Ridged contribution
            _ridged = new FastNoiseLiteSource(seed + 4, ridgedType, TerrainScale * 0.6f, 4, 2.0f, 0.5f);
        }

        // Base infinite height function (no island falloff)
        public float ComputeHeight(float worldX, float worldZ)
        {
            // Domain warp to break up grid-aligned patterns
            float warp = 0.35f;
            float wa = _warpA.GetValue3D(worldX * TerrainScale * 0.5f + 100.0f, 0.0f, worldZ * TerrainScale * 0.5f + 100.0f);
            float wb = _warpB.GetValue3D(worldX * TerrainScale * 0.5f - 100.0f, 0.0f, worldZ * TerrainScale * 0.5f - 100.0f);
            float wxWarp = worldX + ((wa + 1.0f) * 0.5f - 0.5f) * warp * 20f;
            float wzWarp = worldZ + ((wb + 1.0f) * 0.5f - 0.5f) * warp * 20f;

            // Layered noises
            float macro = (_macroFbm.GetValue3D(wxWarp * MacroScale, 0.0f, wzWarp * MacroScale) + 1f) * 0.5f; // 0..1
            float baseVal = (_baseFbm.GetValue3D(wxWarp * TerrainScale, 0.0f, wzWarp * TerrainScale) + 1f) * 0.5f;
            float ridgedVal = (_ridged.GetValue3D(wxWarp * TerrainScale * 0.6f, 0.0f, wzWarp * TerrainScale * 0.6f) + 1f) * 0.5f;
            float detail = (_detailFbm.GetValue3D(wxWarp * DetailScale, 0.0f, wzWarp * DetailScale) + 1f) * 0.5f;

            // Simple analytic hills fallback (guarantees visible relief even if noise degenerates)
            float sinHills = (MathF.Sin(wxWarp * 0.01f) + MathF.Cos(wzWarp * 0.012f)) * 0.25f +
                             (MathF.Sin(wxWarp * 0.035f + 1.3f) * MathF.Cos(wzWarp * 0.028f - 0.7f)) * 0.125f; // ~[-0.375,0.375]
            float sin01 = Math.Clamp((sinHills + 0.5f), 0f, 1f);

            // Terracing function to create geological layers subtly
            float terrace(float v, float steps, float strength)
            {
                float t = MathF.Round(v * steps) / steps;
                return v + (t - v) * strength;
            }

            // Combine layers (stronger macro and vertical exaggeration)
            float h01 = 0.45f * baseVal + 0.5f * macro + 0.3f * ridgedVal + 0.08f * detail;
            // Mix in analytic fallback to ensure non-flat result
            h01 = h01 * 0.85f + sin01 * 0.15f;
            h01 = Math.Clamp(h01, 0f, 1f);
            h01 = terrace(h01, 8f, 0.28f);

            // Vertical exaggeration for clarity
            float exaggeration = 1.35f;
            return h01 * TerrainAmplitude * exaggeration;
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
