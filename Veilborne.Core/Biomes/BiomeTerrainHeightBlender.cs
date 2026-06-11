using System;
using System.Numerics;
using Veilborne.Interfaces;

namespace Veilborne.Biomes
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
                float warpX = ValueNoise2D(worldX * wf, worldZ * wf, seedW) * n.WarpStrength * 55f;
                float warpZ = ValueNoise2D(worldX * wf + 1000f, worldZ * wf + 1000f, seedW + 7) * n.WarpStrength * 55f;
                wx += warpX;
                wz += warpZ;
            }

            // Continental noise: very low-frequency macro-scale height modulation
            float continentalOffset = 0f;
            if (n.ContinentalScale > 0.001f)
            {
                float cf = freq * MathF.Max(0.01f, n.ContinentalFrequency);
                continentalOffset = Fbm(wx, wz, seedA + 200, cf, 4, 0.52f, 2f) * n.ContinentalScale * 12f;
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

            float elevationBias = (b.Altitude - 0.5f) * 14f + biome.BaseHeight * 5f;
            float roughAmp = 2.2f + b.Roughness * 7f + MathF.Max(0f, n.Detail) * 2.5f;
            float ridgeAmp = (1.2f + b.Roughness * 3.5f) * (1f + MathF.Max(0f, n.HeightScale) * 3f);
            float moistureFlatten = 1f - b.Moisture * 0.45f;
            float fertilitySoften = 1f - b.Fertility * 0.25f;

            // Blend FBM, ridge, and billow according to weights
            float rw = Math.Clamp(n.RidgeWeight, 0f, 1f);
            float localShape = fbm * roughAmp * moistureFlatten * fertilitySoften * (1f - rw * 0.5f)
                             + ridge * ridgeAmp * (0.5f + rw * 0.5f)
                             + billow * roughAmp * bw * 0.6f
                             + continentalOffset;

            // Canyon carving: narrow ridge-aligned gullies and river cuts
            if (n.CanyonDepth > 0.001f)
            {
                localShape -= SampleCanyonCut(wx, wz, seedB + 777, freq, n, roughAmp);
            }

            // Rolling hills: readable golden waves and gentle savanna undulation
            if (n.RollingHillsAmplitude > 0.001f)
            {
                localShape += SampleRollingHills(wx, wz, seedC + 333, freq, n);
            }

            // Basin bowls: marsh depressions and volcanic caldera floors
            if (n.BasinStrength > 0.001f)
            {
                localShape -= SampleBasinBowl(wx, wz, seedA + 444, freq, n, roughAmp);
            }

            // Pond/lake depressions: glowing pools, acid pools, sinkholes
            if (n.PondDepth > 0.001f)
            {
                localShape -= SamplePondDepressions(wx, wz, seedC + 999, freq, n);
            }

            // Volcanic/magical pulse: molten roots bulging beneath scorched ground
            if (n.VolcanicPulse > 0.001f)
            {
                localShape += SampleVolcanicPulse(wx, wz, seedB + 888, freq, n, roughAmp);
            }

            // Dune directional noise: wind-sculpted sand dune or rolling hill patterns
            if (n.DuneFrequency > 0.001f && n.DuneAmplitude > 0.001f)
            {
                localShape += SampleDuneField(wx, wz, seedC + 50, freq, n);
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
                float cliffBand = MathF.Max(0f, MathF.Abs(cliffNoise) - 0.25f) * 4f;
                localShape += cliffBand * n.OverhangStrength * roughAmp * 0.65f;
            }

            // Custom stacked noise layers (data-driven from JSON)
            if (biome.NoiseLayers is { Count: > 0 } layers)
            {
                foreach (var layer in layers)
                {
                    if (!layer.Enabled || MathF.Abs(layer.Amplitude) < 0.001f) continue;
                    int layerSeed = seedA + layer.Seed + HashCode.Combine(layer.Type, layer.Frequency);
                    float layerFreq = freq * layer.Frequency;
                    float layerValue = EvaluateNoiseLayer(layer, wx, wz, layerSeed, layerFreq, roughAmp, ridgeAmp) * layer.Amplitude + layer.Offset;
                    localShape = BlendLayerValue(localShape, layerValue, layer.BlendMode);
                }
            }

            float heightMult = biome.HeightMultiplier > 0.01f ? biome.HeightMultiplier : 1f;
            float h = baseHeight + (elevationBias + localShape) * heightMult;

            // Wetland/glass-sea flattening for readable low terrain
            float wetlandFlatten = n.WetlandFlatten > 0.001f
                ? n.WetlandFlatten
                : (b.Moisture > 0.88f ? (b.Moisture - 0.88f) * 4f : 0f);
            if (wetlandFlatten > 0.001f)
            {
                float anchor = baseHeight + elevationBias * 0.35f;
                h = anchor + (h - anchor) * (1f - Math.Clamp(wetlandFlatten, 0f, 0.92f));
            }

            // Floating island platforms for surreal/void biomes
            if (n.IslandScale > 0.001f)
            {
                h = ApplyFloatingIslands(h, wx, wz, seedW + 666, freq, n, baseHeight + elevationBias);
            }

            // Strata cliffs: flat-topped elevation tiers with near-vertical faces
            if (n.CliffStrength > 0.001f)
            {
                h = ApplyStrataCliffs(h, wx, wz, seedA + 512, freq, n, roughAmp + ridgeAmp * 0.5f);
            }

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
                float range = roughAmp * 2.5f + ridgeAmp;
                float stepSize = range / steps;
                if (stepSize > 0.01f)
                {
                    float terraced = MathF.Floor(h / stepSize + 0.5f) * stepSize;
                    h = h + (terraced - h) * Math.Clamp(n.TerracingStrength, 0f, 1f);
                }
            }

            // Plateau: flatten terrain above a threshold
            if (n.PlateauLevel > 0.001f)
            {
                float plateauH = baseHeight + elevationBias + n.PlateauLevel * (roughAmp + ridgeAmp);
                if (h > plateauH)
                    h = plateauH + (h - plateauH) * 0.12f;
            }

            return h;
        }

        /// <summary>Quantize height into flat-topped strata with steep cliff faces.</summary>
        private static float ApplyStrataCliffs(float height, float wx, float wz, int seed, float freq, BiomeNoiseModifiers n, float ampScale)
        {
            float strength = Math.Clamp(n.CliffStrength, 0f, 1f);
            int tiers = Math.Max(2, n.CliffTiers > 0 ? n.CliffTiers : 4);
            float cf = freq * MathF.Max(0.08f, n.CliffFrequency);
            float regional = Fbm(wx, wz, seed, cf, 3, 0.48f, 2.1f);
            float adjusted = height + regional * ampScale * 0.35f;

            float stepSize = ampScale * strength * 2.2f / tiers + 0.35f;
            float stepIndex = adjusted / stepSize;
            float floorStep = MathF.Floor(stepIndex);
            float frac = stepIndex - floorStep;

            float edge = Math.Clamp(n.CliffSharpness > 0 ? n.CliffSharpness : 0.1f, 0.02f, 0.35f);
            float shapedFrac = frac < edge ? 0f : (frac - edge) / (1f - edge);
            float stratified = (floorStep + shapedFrac) * stepSize;

            return height + (stratified - height) * strength;
        }

        private static float SampleCanyonCut(float wx, float wz, int seed, float freq, BiomeNoiseModifiers n, float roughAmp)
        {
            float cf = freq * MathF.Max(0.15f, n.CanyonFrequency);
            float canyonRidge = Fbm(wx, wz, seed, cf, 4, 0.5f, 2.3f);
            float canyonLine = 1f - MathF.Abs(canyonRidge);
            canyonLine = MathF.Pow(Math.Clamp(canyonLine, 0f, 1f), 1.6f);

            float width = Math.Clamp(n.CanyonWidth > 0 ? n.CanyonWidth : 0.35f, 0.08f, 0.85f);
            float threshold = 1f - width;
            if (canyonLine <= threshold) return 0f;

            float depth = (canyonLine - threshold) / width;
            return depth * n.CanyonDepth * roughAmp;
        }

        private static float SampleRollingHills(float wx, float wz, int seed, float freq, BiomeNoiseModifiers n)
        {
            float rf = freq * MathF.Max(0.2f, n.RollingHillsFrequency) * 2.5f;
            float waveA = MathF.Sin(wx * rf + ValueNoise2D(wx * rf * 0.2f, wz * rf * 0.2f, seed) * 1.5f);
            float waveB = MathF.Cos(wz * rf * 0.85f + ValueNoise2D(wx * rf * 0.15f, wz * rf * 0.15f, seed + 3) * 1.2f);
            float blend = Fbm(wx, wz, seed + 11, rf * 0.35f, 2, 0.45f, 2f) * 0.35f;
            return (waveA * 0.55f + waveB * 0.45f + blend) * n.RollingHillsAmplitude;
        }

        private static float SampleBasinBowl(float wx, float wz, int seed, float freq, BiomeNoiseModifiers n, float roughAmp)
        {
            float bf = freq * 0.18f;
            float bowlNoise = Fbm(wx, wz, seed, bf, 3, 0.5f, 2f);
            float bowl = MathF.Max(0f, -bowlNoise);
            bowl = bowl * bowl * n.BasinStrength * roughAmp * 0.85f;
            float rim = MathF.Max(0f, bowlNoise - 0.15f) * n.BasinStrength * roughAmp * 0.2f;
            return bowl - rim * 0.3f;
        }

        private static float SamplePondDepressions(float wx, float wz, int seed, float freq, BiomeNoiseModifiers n)
        {
            float pf = freq * MathF.Max(0.5f, n.PondFrequency) * 2.2f;
            float dist = WorleyNoise2D(wx * pf, wz * pf, seed);
            float radius = 0.32f + ValueNoise2D(wx * pf * 0.5f, wz * pf * 0.5f, seed + 5) * 0.08f;
            if (dist >= radius) return 0f;
            float t = 1f - dist / radius;
            return t * t * n.PondDepth;
        }

        private static float SampleVolcanicPulse(float wx, float wz, int seed, float freq, BiomeNoiseModifiers n, float roughAmp)
        {
            float pf = freq * 1.4f;
            float pulse = Fbm(wx, wz, seed, pf, 3, 0.55f, 2.2f);
            float bulge = MathF.Max(0f, pulse) * MathF.Max(0f, pulse);
            float crack = (1f - MathF.Abs(Fbm(wx, wz, seed + 40, pf * 2.2f, 2, 0.5f, 2.5f))) * 0.4f;
            return (bulge + crack) * n.VolcanicPulse * roughAmp * 0.35f;
        }

        private static float SampleDuneField(float wx, float wz, int seed, float freq, BiomeNoiseModifiers n)
        {
            float dirRad = n.DuneDirection * MathF.PI / 180f;
            float cosD = MathF.Cos(dirRad);
            float sinD = MathF.Sin(dirRad);
            float perpX = -sinD;
            float perpZ = cosD;
            float projected = wx * cosD + wz * sinD;
            float cross = wx * perpX + wz * perpZ;
            float duneFreq = freq * n.DuneFrequency * 3.2f;
            float envelope = 0.65f + ValueNoise2D(wx * duneFreq * 0.25f, wz * duneFreq * 0.25f, seed) * 0.35f;
            float crestNoise = ValueNoise2D(wx * duneFreq * 0.4f, wz * duneFreq * 0.4f, seed + 17) * 0.25f;
            float duneVal = MathF.Sin(projected * duneFreq + crestNoise * MathF.PI * 2f) * 0.5f + 0.5f;
            float barchan = MathF.Exp(-MathF.Abs(cross) * duneFreq * 0.18f) * 0.35f + 0.65f;
            return duneVal * n.DuneAmplitude * envelope * barchan;
        }

        private static float ApplyFloatingIslands(float height, float wx, float wz, int seed, float freq, BiomeNoiseModifiers n, float platformBase)
        {
            float islandFreq = freq * MathF.Max(0.15f, n.IslandScale) * 1.8f;
            float cellDist = WorleyNoise2D(wx * islandFreq, wz * islandFreq, seed);
            float islandRadius = 0.42f;
            float edge = Math.Clamp((islandRadius - cellDist) / 0.12f, 0f, 1f);
            edge = edge * edge * (3f - 2f * edge);
            float drop = MathF.Max(0.5f, n.IslandDrop);
            float voidFloor = platformBase - drop;
            return voidFloor + (height - voidFloor) * edge;
        }

        /// <summary>Evaluate a single data-driven noise layer by type.</summary>
        private static float EvaluateNoiseLayer(NoiseLayerConfig layer, float wx, float wz, int seed, float freq, float roughAmp, float ridgeAmp)
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
                "cliff" => EvaluateCliffLayer(wx, wz, seed, freq, oct, roughAmp + ridgeAmp * 0.5f),
                "canyon" => EvaluateCanyonLayer(wx, wz, seed, freq, oct),
                "pond" => EvaluatePondLayer(wx, wz, seed, freq),
                "rolling" => SampleRollingHills(wx, wz, seed, freq, new BiomeNoiseModifiers { RollingHillsAmplitude = 1f, RollingHillsFrequency = layer.Frequency }),
                "basin" => EvaluateBasinLayer(wx, wz, seed, freq, roughAmp),
                "island" => EvaluateIslandLayer(wx, wz, seed, freq),
                _ => Fbm(wx, wz, seed, freq, oct, pers, lac), // "perlin" or default
            };
        }

        private static float EvaluateCliffLayer(float wx, float wz, int seed, float freq, int tiers, float ampScale)
        {
            float mask = Fbm(wx, wz, seed, freq * 0.4f, 2, 0.5f, 2f);
            float norm = (mask + 1f) * 0.5f;
            int t = Math.Max(2, tiers);
            float stepped = MathF.Floor(norm * t) / t;
            return stepped * ampScale * 0.5f;
        }

        private static float EvaluateCanyonLayer(float wx, float wz, int seed, float freq, int octaves)
        {
            float ridge = Fbm(wx, wz, seed, freq, octaves, 0.5f, 2.2f);
            return MathF.Pow(Math.Clamp(1f - MathF.Abs(ridge), 0f, 1f), 2f);
        }

        private static float EvaluatePondLayer(float wx, float wz, int seed, float freq)
        {
            float dist = WorleyNoise2D(wx * freq, wz * freq, seed);
            float radius = 0.34f;
            if (dist >= radius) return 0f;
            float t = 1f - dist / radius;
            return -(t * t);
        }

        private static float EvaluateBasinLayer(float wx, float wz, int seed, float freq, float roughAmp)
        {
            float bowlNoise = Fbm(wx, wz, seed, freq * 0.35f, 3, 0.5f, 2f);
            return -MathF.Max(0f, -bowlNoise) * roughAmp * 0.4f;
        }

        private static float EvaluateIslandLayer(float wx, float wz, int seed, float freq)
        {
            float dist = WorleyNoise2D(wx * freq, wz * freq, seed);
            return Math.Clamp((0.4f - dist) / 0.15f, 0f, 1f);
        }

        /// <summary>Combine a noise layer value with the existing terrain shape.</summary>
        private static float BlendLayerValue(float existing, float layerValue, string blendMode)
        {
            return blendMode?.ToLowerInvariant() switch
            {
                "multiply" => existing * (1f + layerValue),
                "max" => MathF.Max(existing, layerValue),
                "min" => layerValue < 0f ? existing + layerValue : MathF.Min(existing, layerValue),
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
