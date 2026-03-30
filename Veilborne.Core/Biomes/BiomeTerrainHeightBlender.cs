using System.Numerics;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.Biomes
{
    internal static class BiomeTerrainHeightBlender
    {
        [ThreadStatic] private static BiomeWeight[]? _cachedWeights;

        public static float ComputeHeight(
            float worldX,
            float worldZ,
            float baseHeight,
            IBiomeProvider biomeProvider,
            ITerrainGenerator terrain)
        {
            var p = new Vector2(worldX, worldZ);

            // Reuse thread-local array to avoid per-call allocation on hot path
            var weights = _cachedWeights ??= new BiomeWeight[4];
            biomeProvider.GetBlendWeightsAt(p, terrain, weights, out int count, 4);

            if (count <= 1)
            {
                var biome = count == 1 ? weights[0].Biome : biomeProvider.GetBiomeAt(p, terrain);
                return ApplyBiome(biome.Data, worldX, worldZ, baseHeight);
            }

            // Weighted blend across all contributing biomes
            float blendedHeight = 0f;
            for (int i = 0; i < count; i++)
            {
                float h = ApplyBiome(weights[i].Biome.Data, worldX, worldZ, baseHeight);
                blendedHeight += h * weights[i].Weight;
            }
            return blendedHeight;
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
            int seedW = HashBiomeSeed(biome.Id, 53);
            int seedC = HashBiomeSeed(biome.Id, 89);

            // Domain warping for organic terrain shapes
            float wx = worldX;
            float wz = worldZ;
            if (n.WarpStrength > 0.001f)
            {
                float wf = freq * MathF.Max(0.1f, n.WarpFrequency);
                float warpX = ValueNoise2D(worldX * wf, worldZ * wf, seedW) * n.WarpStrength * 40f;
                float warpZ = ValueNoise2D(worldX * wf + 1000f, worldZ * wf + 1000f, seedW + 7) * n.WarpStrength * 40f;
                wx += warpX;
                wz += warpZ;
            }

            // Continental noise: very low-frequency macro-scale height modulation
            float continentalOffset = 0f;
            if (n.ContinentalScale > 0.001f)
            {
                float cf = freq * MathF.Max(0.01f, n.ContinentalFrequency);
                continentalOffset = Fbm(wx, wz, seedA + 200, cf, 3, 0.5f, 2f) * n.ContinentalScale * 8f;
            }

            float fbm = Fbm(wx, wz, seedA, freq, octaves, persistence, lacunarity);

            // Ridge noise with configurable sharpness
            float ridgeSharpness = Math.Clamp(n.RidgeSharpness > 0f ? n.RidgeSharpness : 1.5f, 0.5f, 4f);
            float ridge = 1f - MathF.Abs(Fbm(wx, wz, seedB, freq * 1.8f, Math.Max(2, octaves - 1), persistence * 0.9f, lacunarity));
            ridge = MathF.Pow(Math.Clamp(ridge, 0f, 1f), ridgeSharpness);

            // Billow noise: abs(FBM) for rounded puffy shapes
            float billow = 0f;
            float bw = Math.Clamp(n.BillowWeight, 0f, 1f);
            if (bw > 0.001f)
            {
                float rawBillow = Fbm(wx + 500f, wz + 500f, seedC, freq * 1.2f, Math.Max(2, octaves - 1), persistence, lacunarity);
                billow = MathF.Abs(rawBillow);
            }

            // Erosion: smooth FBM peaks, deepen valleys
            if (n.ErosionStrength > 0.001f)
            {
                float e = Math.Clamp(n.ErosionStrength, 0f, 1f);
                fbm = fbm > 0 ? fbm * (1f - e * 0.3f) : fbm * (1f + e * 0.4f);
                ridge *= 1f - e * 0.25f;
            }

            // Valley depth bias: shift negative FBM values deeper
            if (MathF.Abs(n.ValleyDepthBias) > 0.001f)
            {
                float vdb = Math.Clamp(n.ValleyDepthBias, -1f, 1f);
                if (fbm < 0f)
                    fbm *= 1f + vdb * 0.6f;
            }

            float elevationBias = (b.Altitude - 0.5f) * 10f + biome.BaseHeight * 4f;
            float roughAmp = 1.5f + b.Roughness * 5.5f + MathF.Max(0f, n.Detail) * 2f;
            float ridgeAmp = (0.8f + b.Roughness * 2.2f) * (1f + MathF.Max(0f, n.HeightScale) * 2.5f);
            float moistureFlatten = 1f - b.Moisture * 0.35f;
            float fertilitySoften = 1f - b.Fertility * 0.2f;

            // Blend FBM, ridge, and billow according to weights
            float rw = Math.Clamp(n.RidgeWeight, 0f, 1f);
            float localShape = fbm * roughAmp * moistureFlatten * fertilitySoften * (1f - rw * 0.5f)
                             + ridge * ridgeAmp * (0.5f + rw * 0.5f)
                             + billow * roughAmp * bw * 0.6f
                             + continentalOffset;

            // Dune directional noise: wind-sculpted sand dune or rolling hill patterns
            if (n.DuneFrequency > 0.001f && n.DuneAmplitude > 0.001f)
            {
                float dirRad = n.DuneDirection * MathF.PI / 180f;
                float projected = wx * MathF.Cos(dirRad) + wz * MathF.Sin(dirRad);
                float duneFreq = freq * n.DuneFrequency * 3f;
                float perpNoise = ValueNoise2D(wx * duneFreq * 0.3f, wz * duneFreq * 0.3f, seedC + 50) * 0.3f;
                float duneVal = MathF.Sin(projected * duneFreq + perpNoise * MathF.PI * 2f) * 0.5f + 0.5f;
                localShape += duneVal * n.DuneAmplitude;
            }

            // Crater noise: Worley/cellular-like depressions with raised rims
            if (n.CraterFrequency > 0.001f && (n.CraterDepth > 0.001f || n.CraterRimHeight > 0.001f))
            {
                float craterFreq = freq * n.CraterFrequency * 2f;
                float cellDist = WorleyNoise2D(wx * craterFreq, wz * craterFreq, seedC + 100);
                float craterProfile = Math.Clamp(cellDist * 2f - 0.3f, 0f, 1f);
                float rimProfile = MathF.Max(0f, 1f - MathF.Abs(cellDist - 0.4f) * 4f);
                localShape -= (1f - craterProfile) * n.CraterDepth;
                localShape += rimProfile * n.CraterRimHeight;
            }

            // Micro-detail noise: high-frequency surface variation
            if (n.MicroDetailFrequency > 0.001f && n.MicroDetailAmplitude > 0.001f)
            {
                float microFreq = freq * MathF.Max(1f, n.MicroDetailFrequency) * 4f;
                float micro = ValueNoise2D(wx * microFreq, wz * microFreq, seedC + 31);
                localShape += micro * Math.Clamp(n.MicroDetailAmplitude, 0f, 2f);
            }

            // Overhang cliff displacement: creates steep cliff-band profiles
            if (n.OverhangStrength > 0.001f)
            {
                float ohFreq = freq * MathF.Max(0.5f, n.OverhangFrequency) * 6f;
                float cliffNoise = ValueNoise2D(wx * ohFreq, wz * ohFreq, seedW + 200);
                float cliffBand = MathF.Max(0f, MathF.Abs(cliffNoise) - 0.3f) * 3f;
                localShape += cliffBand * n.OverhangStrength * roughAmp * 0.4f;
            }

            // Custom stacked noise layers (data-driven from JSON)
            if (biome.NoiseLayers is { Count: > 0 } layers)
            {
                foreach (var layer in layers)
                {
                    if (!layer.Enabled || layer.Amplitude < 0.001f) continue;
                    int layerSeed = seedA + layer.Seed + HashCode.Combine(layer.Type, layer.Frequency);
                    float layerFreq = freq * layer.Frequency;
                    float layerValue = EvaluateNoiseLayer(layer, wx, wz, layerSeed, layerFreq) * layer.Amplitude + layer.Offset;
                    localShape = BlendLayerValue(localShape, layerValue, layer.BlendMode);
                }
            }

            float h = baseHeight + elevationBias + localShape;

            // Slope-aware erosion: compute local gradient and apply extra smoothing on steep areas
            if (n.SlopeErosionScale > 0.001f)
            {
                float gradX = ValueNoise2D((wx + 0.5f) * freq, wz * freq, seedA) - ValueNoise2D((wx - 0.5f) * freq, wz * freq, seedA);
                float gradZ = ValueNoise2D(wx * freq, (wz + 0.5f) * freq, seedA) - ValueNoise2D(wx * freq, (wz - 0.5f) * freq, seedA);
                float slopeProxy = MathF.Sqrt(gradX * gradX + gradZ * gradZ);
                float slopeErosion = Math.Clamp(slopeProxy * n.SlopeErosionScale * 3f, 0f, 0.5f);
                h -= slopeErosion * roughAmp * 0.3f;
            }

            // Terracing: snap height to discrete steps for mesa/terrace biomes
            if (n.TerracingStrength > 0.001f)
            {
                int steps = Math.Max(2, n.TerracingSteps);
                float range = roughAmp * 2f + ridgeAmp;
                float stepSize = range / steps;
                if (stepSize > 0.01f)
                {
                    float terraced = MathF.Round(h / stepSize) * stepSize;
                    h = h + (terraced - h) * Math.Clamp(n.TerracingStrength, 0f, 1f);
                }
            }

            // Plateau: flatten terrain above a threshold
            if (n.PlateauLevel > 0.001f)
            {
                float plateauH = baseHeight + elevationBias + n.PlateauLevel * (roughAmp + ridgeAmp);
                if (h > plateauH)
                    h = plateauH + (h - plateauH) * 0.15f;
            }

            return h;
        }

        /// <summary>Evaluate a single data-driven noise layer by type.</summary>
        private static float EvaluateNoiseLayer(NoiseLayerConfig layer, float wx, float wz, int seed, float freq)
        {
            int oct = Math.Clamp(layer.Octaves, 1, 8);
            float pers = Math.Clamp(layer.Persistence, 0.1f, 0.9f);
            float lac = Math.Clamp(layer.Lacunarity, 1.2f, 4f);

            return layer.Type?.ToLowerInvariant() switch
            {
                "ridge" => 1f - MathF.Abs(Fbm(wx, wz, seed, freq, oct, pers * 0.9f, lac)),
                "billow" => MathF.Abs(Fbm(wx, wz, seed, freq, oct, pers, lac)),
                "value" => ValueNoise2D(wx * freq, wz * freq, seed),
                "worley" => WorleyNoise2D(wx * freq, wz * freq, seed),
                _ => Fbm(wx, wz, seed, freq, oct, pers, lac), // "perlin" or default
            };
        }

        /// <summary>Combine a noise layer value with the existing terrain shape.</summary>
        private static float BlendLayerValue(float existing, float layerValue, string blendMode)
        {
            return blendMode?.ToLowerInvariant() switch
            {
                "multiply" => existing * (1f + layerValue),
                "max" => MathF.Max(existing, layerValue),
                "min" => MathF.Min(existing, layerValue),
                "screen" => existing + layerValue - existing * layerValue,
                _ => existing + layerValue, // "add" or default
            };
        }

        /// <summary>Simple 2D Worley (cellular) noise returning distance to nearest cell point.</summary>
        private static float WorleyNoise2D(float x, float z, int seed)
        {
            int cellX = (int)MathF.Floor(x);
            int cellZ = (int)MathF.Floor(z);
            float minDist = float.MaxValue;

            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = cellX + dx;
                    int cz = cellZ + dz;
                    float px = cx + (HashToSignedUnit(cx, cz, seed) * 0.5f + 0.5f);
                    float pz = cz + (HashToSignedUnit(cx, cz, seed + 31) * 0.5f + 0.5f);
                    float dist = (x - px) * (x - px) + (z - pz) * (z - pz);
                    if (dist < minDist) minDist = dist;
                }
            }

            return MathF.Sqrt(minDist);
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
