using System;
using System.Numerics;
using Veilborne.Interfaces;

namespace Veilborne.Biomes
{
    internal static class BiomeTerrainHeightBlender
    {
        public static float ComputeHeight(
            float worldX,
            float worldZ,
            float baseHeight,
            IBiomeProvider biomeProvider,
            ITerrainGenerator terrain)
        {
            var p = new Vector2(worldX, worldZ);
            // Keep this path very cheap: it runs for every height sample while building chunks.
            // Visual biome blending happens in renderer/chunk metadata, not per-sample height evaluation.
            var primary = biomeProvider.GetBiomeAt(p, terrain);
            return ApplyBiome(primary.Data, worldX, worldZ, baseHeight);
        }

        private static float ApplyBiome(BiomeData biome, float worldX, float worldZ, float baseHeight)
        {
            var b = biome.ProceduralData.Base;
            var n = biome.ProceduralData.NoiseModifiers;

            float freq = 0.0011f * MathF.Max(0.2f, n.Frequency);
            int octaves = Math.Clamp(2 + (int)MathF.Round(MathF.Max(0f, n.Detail) * 3f), 2, 6);
            float persistence = Math.Clamp(n.Persistence <= 0f ? 0.5f : n.Persistence, 0.2f, 0.85f);
            float lacunarity = Math.Clamp(n.Lacunarity <= 0f ? 2f : n.Lacunarity, 1.5f, 3.2f);

            int seedA = HashBiomeSeed(biome.Id, 17);
            int seedB = HashBiomeSeed(biome.Id, 71);

            float fbm = Fbm(worldX, worldZ, seedA, freq, octaves, persistence, lacunarity);
            float ridge = 1f - MathF.Abs(Fbm(worldX, worldZ, seedB, freq * 1.8f, Math.Max(2, octaves - 1), persistence * 0.9f, lacunarity));
            ridge = MathF.Pow(Math.Clamp(ridge, 0f, 1f), 1.5f);

            float elevationBias = (b.Altitude - 0.5f) * 10f + biome.BaseHeight * 4f;
            float roughAmp = 1.5f + b.Roughness * 5.5f + MathF.Max(0f, n.Detail) * 2f;
            float ridgeAmp = (0.8f + b.Roughness * 2.2f) * (1f + MathF.Max(0f, n.HeightScale) * 2.5f);
            float moistureFlatten = 1f - b.Moisture * 0.35f;
            float fertilitySoften = 1f - b.Fertility * 0.2f;

            float localShape = fbm * roughAmp * moistureFlatten * fertilitySoften + ridge * ridgeAmp;
            return baseHeight + elevationBias + localShape;
        }

        private static int HashBiomeSeed(string biomeId, int salt)
            => HashCode.Combine(biomeId.ToLowerInvariant(), salt);

        private static float Fbm(float x, float z, int seed, float frequency, int octaves, float persistence, float lacunarity)
        {
            float amplitude = 1f;
            float sum = 0f;
            float norm = 0f;
            float fx = x * frequency;
            float fz = z * frequency;

            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise2D(fx, fz, seed + i * 131) * amplitude;
                norm += amplitude;
                amplitude *= persistence;
                fx *= lacunarity;
                fz *= lacunarity;
            }

            if (norm <= 1e-6f) return 0f;
            return sum / norm;
        }

        private static float ValueNoise2D(float x, float z, int seed)
        {
            int x0 = (int)MathF.Floor(x);
            int z0 = (int)MathF.Floor(z);
            int x1 = x0 + 1;
            int z1 = z0 + 1;
            float tx = x - x0;
            float tz = z - z0;
            float sx = SmoothStep01(tx);
            float sz = SmoothStep01(tz);

            float v00 = HashToSignedUnit(x0, z0, seed);
            float v10 = HashToSignedUnit(x1, z0, seed);
            float v01 = HashToSignedUnit(x0, z1, seed);
            float v11 = HashToSignedUnit(x1, z1, seed);
            float nx0 = Lerp(v00, v10, sx);
            float nx1 = Lerp(v01, v11, sx);
            return Lerp(nx0, nx1, sz);
        }

        private static float HashToSignedUnit(int x, int z, int seed)
        {
            unchecked
            {
                int h = seed;
                h ^= x * 374761393;
                h = (h << 13) ^ h;
                h ^= z * 668265263;
                h = (h ^ (h >> 17)) * 1274126177;
                return ((h & 0x7fffffff) / (float)int.MaxValue) * 2f - 1f;
            }
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float SmoothStep01(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return t * t * (3f - 2f * t);
        }
    }
}
