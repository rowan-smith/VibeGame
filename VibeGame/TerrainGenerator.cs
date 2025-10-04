using System.Numerics;

namespace VibeGame
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

                    // Domain warp to break up grid-aligned patterns
                    float warp = 0.35f;
                    float wxWarp = wx + (Noise.Fbm(wx * TerrainScale * 0.5f + 100, wz * TerrainScale * 0.5f + 100, 3, 2.0f, 0.5f) - 0.5f) * warp * 20f;
                    float wzWarp = wz + (Noise.Fbm(wx * TerrainScale * 0.5f - 100, wz * TerrainScale * 0.5f - 100, 3, 2.0f, 0.5f) - 0.5f) * warp * 20f;

                    float baseMountains = Noise.Fbm(wxWarp * TerrainScale, wzWarp * TerrainScale, TerrainOctaves, TerrainLacunarity, TerrainGain);
                    float ridged = Noise.RidgedFbm(wxWarp * TerrainScale * 0.6f, wzWarp * TerrainScale * 0.6f, 4, 2.0f, 0.5f);

                    // Blend: base + some ridged for sharp peaks
                    float h01 = baseMountains * 0.7f + ridged * 0.6f;

                    // Edge falloff island
                    float nx = (x / (float)(TerrainSize - 1)) * 2f - 1f;
                    float nz = (z / (float)(TerrainSize - 1)) * 2f - 1f;
                    float r = MathF.Sqrt(nx * nx + nz * nz);
                    float falloff = Math.Clamp(1f - MathF.Pow(MathF.Max(0f, r - 0.6f) / 0.4f, 2f), 0f, 1f);

                    heights[x, z] = (h01 * falloff) * TerrainAmplitude;
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

        // Local noise implementation
        private static class Noise
        {
            private static readonly int[] Perm = BuildPerm();
            private static int[] BuildPerm()
            {
                int[] p = new int[512];
                int[] basePerm = {
                    151,160,137,91,90,15,
                    131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,
                    190, 6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,
                    88,237,149,56,87,174,20,125,136,171,168, 68,175,74,165,71,134,139,48,27,166,
                    77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,
                    102,143,54, 65,25,63,161, 1,216,80,73,209,76,132,187,208, 89,18,169,200,196,
                    135,130,116,188,159,86,164,100,109,198,173,186, 3,64,52,217,226,250,124,123,
                    5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,
                    223,183,170,213,119,248,152, 2,44,154,163, 70,221,153,101,155,167, 43,172,9,
                    129,22,39,253, 19,98,108,110,79,113,224,232,178,185, 112,104,218,246,97,228,
                    251,34,242,193,238,210,144,12,191,179,162,241, 81,51,145,235,249,14,239,107,
                    49,192,214, 31,181,199,106,157,184, 84,204,176,115,121,50,45,127, 4,150,254,
                    138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180
                };
                for (int i = 0; i < 256; i++) { p[i] = basePerm[i]; p[i + 256] = basePerm[i]; }
                return p;
            }

            private static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);
            private static float Lerp(float a, float b, float t) => a + (b - a) * t;
            private static float Grad(int hash, float x, float y)
            {
                int h = hash & 7;
                float u = h < 4 ? x : y;
                float v = h < 4 ? y : x;
                return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
            }
            public static float Perlin(float x, float y)
            {
                int xi = (int)MathF.Floor(x) & 255;
                int yi = (int)MathF.Floor(y) & 255;
                float xf = x - MathF.Floor(x);
                float yf = y - MathF.Floor(y);
                float u = Fade(xf);
                float v = Fade(yf);
                int aa = Perm[Perm[xi] + yi];
                int ab = Perm[Perm[xi] + yi + 1];
                int ba = Perm[Perm[xi + 1] + yi];
                int bb = Perm[Perm[xi + 1] + yi + 1];
                float x1 = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1, yf), u);
                float x2 = Lerp(Grad(ab, xf, yf - 1), Grad(bb, xf - 1, yf - 1), u);
                float val = Lerp(x1, x2, v);
                return val * 0.7071f;
            }
            public static float Fbm(float x, float y, int octaves, float lacunarity, float gain)
            {
                float sum = 0f, amp = 1f, freq = 1f, max = 0f;
                for (int i = 0; i < octaves; i++)
                {
                    sum += Perlin(x * freq, y * freq) * amp;
                    max += amp;
                    amp *= gain;
                    freq *= lacunarity;
                }
                return (sum / max + 1f) * 0.5f;
            }
            public static float RidgedFbm(float x, float y, int octaves, float lacunarity, float gain)
            {
                float sum = 0f, amp = 0.5f, freq = 1f, weight = 1f;
                for (int i = 0; i < octaves; i++)
                {
                    float n = Perlin(x * freq, y * freq);
                    float signal = 1f - MathF.Abs(n);
                    signal *= signal;
                    signal *= weight;
                    sum += signal * amp;
                    weight = Math.Clamp(signal * 2f, 0f, 1f);
                    freq *= lacunarity;
                    amp *= gain;
                }
                return Math.Clamp(sum, 0f, 1f);
            }
        }
    }
}
