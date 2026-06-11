using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.Terrain;

namespace Veilborne.Biomes
{
    /// <summary>
    /// Helper methods to compute dominant biome over an area in a consistent way across systems.
    /// </summary>
    public static class BiomeSampling
    {
        /// <summary>
        /// Samples a grid of points over the chunk area and returns the most frequent biome.
        /// Optionally applies extra weight to the center sample to stabilize near boundaries.
        /// You can optionally expand the sampled area by a margin in world units to make
        /// the result more stable across adjacent chunks.
        /// </summary>
        /// <param name="provider">Biome provider</param>
        /// <param name="terrain">Terrain generator (can be null if provider ignores it)</param>
        /// <param name="chunkOriginWorld">World origin of the chunk (minX, minZ)</param>
        /// <param name="chunkSize">Chunk size in tiles (not including +1 seam)</param>
        /// <param name="tileSize">Tile size in world units</param>
        /// <param name="samplesPerAxis">Grid resolution (e.g., 9 for 9x9). Minimum 3.</param>
        /// <param name="centerExtraWeight">Additional weight for center sample (e.g., 2.0 adds two extra votes)</param>
        /// <param name="expandWorldMargin">Margin (in world units) to expand the sampled square area on all sides.</param>
        public static IBiome GetDominantBiomeForArea(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 chunkOriginWorld,
            int chunkSize,
            float tileSize,
            int samplesPerAxis = 9,
            float centerExtraWeight = 2f,
            float expandWorldMargin = 0f)
        {
            var (primary, _) = GetDominantAndSecondaryBiomeForArea(provider, terrain, chunkOriginWorld, chunkSize, tileSize, samplesPerAxis, centerExtraWeight, expandWorldMargin);
            return primary;
        }

        /// <summary>
        /// Same as GetDominantBiomeForArea, but also returns the second most frequent biome.
        /// An optional expandWorldMargin makes the result more stable between neighboring chunks
        /// by sampling a slightly larger region.
        /// </summary>
        public static (IBiome primary, IBiome? secondary) GetDominantAndSecondaryBiomeForArea(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 chunkOriginWorld,
            int chunkSize,
            float tileSize,
            int samplesPerAxis = 9,
            float centerExtraWeight = 2f,
            float expandWorldMargin = 0f)
        {
            var (primary, secondary, _, _) = GetDominantAndSecondaryBiomeForAreaWithWeights(
                provider, terrain, chunkOriginWorld, chunkSize, tileSize, samplesPerAxis, centerExtraWeight, expandWorldMargin);
            return (primary, secondary);
        }

        public static (IBiome primary, IBiome? secondary, float primaryWeight, float secondaryWeight) GetDominantAndSecondaryBiomeForAreaWithWeights(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 chunkOriginWorld,
            int chunkSize,
            float tileSize,
            int samplesPerAxis = 9,
            float centerExtraWeight = 2f,
            float expandWorldMargin = 0f)
        {
            samplesPerAxis = Math.Max(3, samplesPerAxis);
            var counts = new Dictionary<string, (IBiome biome, float weight)>(StringComparer.OrdinalIgnoreCase);

            float areaWorldSize = chunkSize * tileSize;
            // Expand the area by the requested margin on all sides
            Vector2 origin = new Vector2(chunkOriginWorld.X - expandWorldMargin, chunkOriginWorld.Y - expandWorldMargin);
            float size = areaWorldSize + expandWorldMargin * 2f;

            float step = size / (samplesPerAxis + 1);

            int centerIdx = (samplesPerAxis + 1) / 2; // integer center index in 1..samplesPerAxis

            for (int j = 1; j <= samplesPerAxis; j++)
            {
                for (int i = 1; i <= samplesPerAxis; i++)
                {
                    float wx = origin.X + i * step;
                    float wz = origin.Y + j * step;
                    var b = provider.GetBiomeAt(new Vector2(wx, wz), terrain!);

                    // Weight samples toward the center to stabilize selection
                    float weight = 1f;
                    if (i == centerIdx && j == centerIdx)
                        weight += Math.Max(0f, centerExtraWeight);

                    if (!counts.TryGetValue(b.Id, out var tuple))
                        counts[b.Id] = (b, weight);
                    else
                        counts[b.Id] = (tuple.biome, tuple.weight + weight);
                }
            }

            if (counts.Count == 0)
            {
                // Fallback to center
                float cx = origin.X + size * 0.5f;
                float cz = origin.Y + size * 0.5f;
                var c = provider.GetBiomeAt(new Vector2(cx, cz), terrain!);
                return (c, null, 1f, 0f);
            }

            var ordered = counts.Values.OrderByDescending(v => v.weight).ToList();
            var primary = ordered[0].biome;
            IBiome? secondary = ordered.Count > 1 ? ordered[1].biome : null;
            float primaryWeight = ordered[0].weight;
            float secondaryWeight = ordered.Count > 1 ? ordered[1].weight : 0f;
            return (primary, secondary, primaryWeight, secondaryWeight);
        }

        /// <summary>
        /// Samples biomes over a fixed world-space square centered at <paramref name="centerWorld"/>.
        /// This is useful for keeping biome assignment stable across rings that use different chunk sizes.
        /// </summary>
        public static IBiome GetDominantBiomeNearPoint(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 centerWorld,
            float halfExtentWorld = 96f,
            int samplesPerAxis = 11,
            float centerExtraWeight = 2f)
        {
            float extent = MathF.Max(1f, halfExtentWorld);
            Vector2 origin = new Vector2(centerWorld.X - extent, centerWorld.Y - extent);
            float worldSize = extent * 2f;
            int pseudoChunkSize = Math.Max(1, samplesPerAxis);
            float pseudoTile = worldSize / pseudoChunkSize;
            return GetDominantBiomeForArea(provider, terrain, origin, pseudoChunkSize, pseudoTile, samplesPerAxis, centerExtraWeight, 0f);
        }

        public static (IBiome primary, IBiome? secondary, float secondaryBlend) GetDominantSecondaryBlendNearPoint(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 centerWorld,
            float halfExtentWorld = 96f,
            int samplesPerAxis = 11,
            float centerExtraWeight = 2f)
        {
            float extent = MathF.Max(1f, halfExtentWorld);
            Vector2 origin = new Vector2(centerWorld.X - extent, centerWorld.Y - extent);
            float worldSize = extent * 2f;
            int pseudoChunkSize = Math.Max(1, samplesPerAxis);
            float pseudoTile = worldSize / pseudoChunkSize;

            var (primary, secondary, primaryWeight, secondaryWeight) = GetDominantAndSecondaryBiomeForAreaWithWeights(
                provider, terrain, origin, pseudoChunkSize, pseudoTile, samplesPerAxis, centerExtraWeight, 0f);

            float blend = 0f;
            float denom = primaryWeight + secondaryWeight;
            if (secondary is not null && denom > 1e-5f)
                blend = Math.Clamp(secondaryWeight / denom, 0f, 1f);

            return (primary, secondary, blend);
        }

        /// <summary>
        /// Samples corners, edge midpoints, and center to catch transitions that fall on
        /// chunk sides between adjacent single-biome chunks.
        /// </summary>
        public static (float maxBlend, IBiome? secondaryBiome) ResolveBoundaryCrossfade(
            SimpleBiomeProvider provider,
            ITerrainGenerator terrain,
            Vector2 origin,
            int width,
            int height,
            float tileSize)
        {
            float chunkW = (width - 1) * tileSize;
            float chunkH = (height - 1) * tileSize;
            float midX = origin.X + chunkW * 0.5f;
            float midZ = origin.Y + chunkH * 0.5f;
            float maxBlend = 0f;
            IBiome? bestSecondary = null;

            void Sample(float wx, float wz)
            {
                var (_, secondary, blend) = provider.GetBiomeBlendAt(new Vector2(wx, wz), terrain);
                if (blend > maxBlend && secondary is not null)
                {
                    maxBlend = blend;
                    bestSecondary = secondary;
                }
            }

            Sample(origin.X, origin.Y);
            Sample(origin.X + chunkW, origin.Y);
            Sample(origin.X, origin.Y + chunkH);
            Sample(origin.X + chunkW, origin.Y + chunkH);
            Sample(midX, origin.Y);
            Sample(midX, origin.Y + chunkH);
            Sample(origin.X, midZ);
            Sample(origin.X + chunkW, midZ);
            Sample(midX, midZ);

            return (maxBlend, bestSecondary);
        }

        /// <summary>
        /// Builds a per-vertex blend map from a coarse grid (default 4x4) of biome samples.
        /// Much faster than per-vertex weight queries while catching edge transitions.
        /// </summary>
        public static float[,] BuildVertexBlendMapGrid(
            SimpleBiomeProvider provider,
            ITerrainGenerator terrain,
            Vector2 origin,
            int width,
            int height,
            float tileSize,
            int samplesPerAxis = 4)
        {
            samplesPerAxis = Math.Max(2, samplesPerAxis);
            var grid = new float[samplesPerAxis, samplesPerAxis];
            float chunkW = (width - 1) * tileSize;
            float chunkH = (height - 1) * tileSize;

            for (int j = 0; j < samplesPerAxis; j++)
            {
                float tz = samplesPerAxis > 1 ? j / (float)(samplesPerAxis - 1) : 0f;
                for (int i = 0; i < samplesPerAxis; i++)
                {
                    float tx = samplesPerAxis > 1 ? i / (float)(samplesPerAxis - 1) : 0f;
                    float wx = origin.X + tx * chunkW;
                    float wz = origin.Y + tz * chunkH;
                    grid[i, j] = SampleBiomeBlend(provider, terrain, wx, wz);
                }
            }

            var map = new float[width, height];
            for (int z = 0; z < height; z++)
            {
                float tz = height > 1 ? z / (float)(height - 1) : 0f;
                float gz = tz * (samplesPerAxis - 1);
                int j0 = (int)MathF.Floor(gz);
                int j1 = Math.Min(j0 + 1, samplesPerAxis - 1);
                float fz = gz - j0;
                for (int x = 0; x < width; x++)
                {
                    float tx = width > 1 ? x / (float)(width - 1) : 0f;
                    float gx = tx * (samplesPerAxis - 1);
                    int i0 = (int)MathF.Floor(gx);
                    int i1 = Math.Min(i0 + 1, samplesPerAxis - 1);
                    float fx = gx - i0;
                    float top = grid[i0, j0] + (grid[i1, j0] - grid[i0, j0]) * fx;
                    float bot = grid[i0, j1] + (grid[i1, j1] - grid[i0, j1]) * fx;
                    map[x, z] = top + (bot - top) * fz;
                }
            }

            return map;
        }

        /// <summary>
        /// Builds a per-vertex blend factor map by sampling biome blend weights at chunk corners
        /// and bilinearly interpolating across the chunk.
        /// </summary>
        public static float[,] BuildVertexBlendMap(
            SimpleBiomeProvider provider,
            ITerrainGenerator terrain,
            Vector2 origin,
            int width,
            int height,
            float tileSize)
        {
            var map = new float[width, height];
            float chunkW = (width - 1) * tileSize;
            float chunkH = (height - 1) * tileSize;

            float b00 = SampleBiomeBlend(provider, terrain, origin.X, origin.Y);
            float b10 = SampleBiomeBlend(provider, terrain, origin.X + chunkW, origin.Y);
            float b01 = SampleBiomeBlend(provider, terrain, origin.X, origin.Y + chunkH);
            float b11 = SampleBiomeBlend(provider, terrain, origin.X + chunkW, origin.Y + chunkH);

            for (int z = 0; z < height; z++)
            {
                float tz = height > 1 ? z / (float)(height - 1) : 0f;
                for (int x = 0; x < width; x++)
                {
                    float tx = width > 1 ? x / (float)(width - 1) : 0f;
                    float top = b00 + (b10 - b00) * tx;
                    float bot = b01 + (b11 - b01) * tx;
                    map[x, z] = top + (bot - top) * tz;
                }
            }

            return map;
        }

        private static float SampleBiomeBlend(SimpleBiomeProvider provider, ITerrainGenerator terrain, float wx, float wz)
        {
            var (_, _, blend) = provider.GetBiomeBlendAt(new Vector2(wx, wz), terrain);
            return blend;
        }

        /// <summary>
        /// Builds a per-vertex biome merge map using world-position blend weights.
        /// Adds noise near transitions so boundaries look organic instead of straight lines.
        /// </summary>
        public static (float[,] mergeMap, IBiome? mergeBiome, float maxMerge) BuildVertexMergeMap(
            SimpleBiomeProvider provider,
            ITerrainGenerator terrain,
            Vector2 origin,
            int width,
            int height,
            float tileSize)
        {
            var map = new float[width, height];
            float maxMerge = 0f;
            IBiome? mergeBiome = null;
            var weights = new BiomeWeight[4];

            for (int z = 0; z < height; z++)
            for (int x = 0; x < width; x++)
            {
                float wx = origin.X + x * tileSize;
                float wz = origin.Y + z * tileSize;
                provider.GetBlendWeightsAt(new Vector2(wx, wz), terrain, weights, out int count, 4);

                float merge = 0f;
                if (count > 1)
                {
                    merge = 1f - weights[0].Weight;
                    if (weights[1].Weight > 0.001f)
                    {
                        float candidate = weights[1].Weight;
                        if (candidate > maxMerge)
                        {
                            maxMerge = candidate;
                            mergeBiome = weights[1].Biome;
                        }
                    }
                    merge = PerturbMergeWeight(wx, wz, merge);
                }

                map[x, z] = merge;
                if (merge > maxMerge)
                    maxMerge = merge;
            }

            return (map, mergeBiome, maxMerge);
        }

        /// <summary>
        /// Picks stable primary/merge biome ids for a chunk using area voting with margin expansion.
        /// </summary>
        public static (string primaryId, string mergeId, float maxMerge) ResolveChunkBiomePair(
            SimpleBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 origin,
            int gridWidth,
            int gridHeight,
            float tileSize,
            float expandMarginTiles = 2f)
        {
            int chunkSize = Math.Max(1, gridWidth - 1);
            float margin = tileSize * MathF.Max(0f, expandMarginTiles);
            var (primary, secondary, primaryWeight, secondaryWeight) = GetDominantAndSecondaryBiomeForAreaWithWeights(
                provider, terrain, origin, chunkSize, tileSize, 7, 2f, margin);

            string primaryId = primary.Id;
            string mergeId = secondary?.Id ?? string.Empty;
            float maxMerge = 0f;
            float denom = primaryWeight + secondaryWeight;
            if (secondary is not null && denom > 1e-5f)
                maxMerge = Math.Clamp(secondaryWeight / denom, 0f, 1f);

            var (boundaryMax, boundarySecondary) = ResolveBoundaryCrossfade(
                provider, terrain!, origin, gridWidth, gridHeight, tileSize);
            if (boundaryMax > maxMerge)
                maxMerge = boundaryMax;
            if (boundarySecondary is not null &&
                !string.Equals(boundarySecondary.Id, primaryId, StringComparison.OrdinalIgnoreCase))
                mergeId = boundarySecondary.Id;

            if (string.Equals(mergeId, primaryId, StringComparison.OrdinalIgnoreCase))
                mergeId = string.Empty;

            return (primaryId, mergeId, maxMerge);
        }

        /// <summary>
        /// World-consistent per-vertex blend toward <paramref name="mergeBiomeId"/> relative to
        /// <paramref name="primaryBiomeId"/>. Uses strided sampling with bilinear upsample when stride &gt; 1.
        /// </summary>
        public static (float[,] map, float maxMerge) BuildChunkPairBlendMap(
            SimpleBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 origin,
            int width,
            int height,
            float tileSize,
            string primaryBiomeId,
            string mergeBiomeId,
            int sampleStride = 1)
        {
            var map = new float[width, height];
            if (string.IsNullOrEmpty(mergeBiomeId) ||
                string.Equals(primaryBiomeId, mergeBiomeId, StringComparison.OrdinalIgnoreCase))
                return (map, 0f);

            sampleStride = Math.Max(1, sampleStride);
            int gridW = (width - 1) / sampleStride + 1;
            int gridH = (height - 1) / sampleStride + 1;
            var coarse = new float[gridW, gridH];
            float maxMerge = 0f;
            var weights = new BiomeWeight[4];

            for (int gz = 0; gz < gridH; gz++)
            {
                int z = Math.Min(gz * sampleStride, height - 1);
                for (int gx = 0; gx < gridW; gx++)
                {
                    int x = Math.Min(gx * sampleStride, width - 1);
                    float wx = origin.X + x * tileSize;
                    float wz = origin.Y + z * tileSize;
                    provider.GetBlendWeightsAt(new Vector2(wx, wz), terrain!, weights, out int count, 4);

                    float primaryW = 0f;
                    float mergeW = 0f;
                    for (int i = 0; i < count; i++)
                    {
                        string id = weights[i].Biome.Id;
                        if (string.Equals(id, primaryBiomeId, StringComparison.OrdinalIgnoreCase))
                            primaryW = weights[i].Weight;
                        else if (string.Equals(id, mergeBiomeId, StringComparison.OrdinalIgnoreCase))
                            mergeW = weights[i].Weight;
                    }

                    float pairSum = primaryW + mergeW;
                    float alpha = pairSum > 1e-5f ? mergeW / pairSum : mergeW;
                    alpha = PerturbMergeWeight(wx, wz, alpha);
                    coarse[gx, gz] = alpha;
                    if (alpha > maxMerge) maxMerge = alpha;
                }
            }

            if (sampleStride == 1)
            {
                for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                    map[x, z] = coarse[x, z];
                return (map, maxMerge);
            }

            for (int z = 0; z < height; z++)
            {
                float gz = height > 1 ? z / (float)(height - 1) : 0f;
                float gzScaled = gz * (gridH - 1);
                int j0 = (int)MathF.Floor(gzScaled);
                int j1 = Math.Min(j0 + 1, gridH - 1);
                float fz = gzScaled - j0;
                for (int x = 0; x < width; x++)
                {
                    float gx = width > 1 ? x / (float)(width - 1) : 0f;
                    float gxScaled = gx * (gridW - 1);
                    int i0 = (int)MathF.Floor(gxScaled);
                    int i1 = Math.Min(i0 + 1, gridW - 1);
                    float fx = gxScaled - i0;
                    float top = coarse[i0, j0] + (coarse[i1, j0] - coarse[i0, j0]) * fx;
                    float bot = coarse[i0, j1] + (coarse[i1, j1] - coarse[i0, j1]) * fx;
                    map[x, z] = top + (bot - top) * fz;
                }
            }

            return (map, maxMerge);
        }

        private static float PerturbMergeWeight(float worldX, float worldZ, float merge)
        {
            if (merge <= 0.001f || merge >= 0.999f)
                return merge;

            float noise = HashNoise(worldX * 0.08f + 41.2f, worldZ * 0.08f - 17.8f);
            float edge = MathF.Min(merge, 1f - merge) * 4f;
            float amplitude = 0.22f * MathF.Min(1f, edge);
            return Math.Clamp(merge + (noise - 0.5f) * 2f * amplitude, 0f, 1f);
        }

        private static float HashNoise(float x, float y)
        {
            uint n = (uint)(x * 127.1f + y * 311.7f);
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731u + 789221u) + 1376312589u) & 0x7FFFFFFF) / (float)0x7FFFFFFF;
        }

        /// <summary>
        /// Resolves biome crossfade from chunk corners. Chunks whose center sits deep inside
        /// one biome can still have non-zero blend at edges that touch a neighbor biome.
        /// </summary>
        public static (float b00, float b10, float b01, float b11, float maxBlend, IBiome? secondaryBiome)
            ResolveCornerCrossfade(
                SimpleBiomeProvider provider,
                ITerrainGenerator terrain,
                Vector2 origin,
                int width,
                int height,
                float tileSize)
        {
            float chunkW = (width - 1) * tileSize;
            float chunkH = (height - 1) * tileSize;

            float maxBlend = 0f;
            IBiome? bestSecondary = null;

            float SampleCorner(float wx, float wz)
            {
                var (_, secondary, blend) = provider.GetBiomeBlendAt(new Vector2(wx, wz), terrain);
                if (blend > maxBlend && secondary is not null)
                {
                    maxBlend = blend;
                    bestSecondary = secondary;
                }
                return blend;
            }

            float b00 = SampleCorner(origin.X, origin.Y);
            float b10 = SampleCorner(origin.X + chunkW, origin.Y);
            float b01 = SampleCorner(origin.X, origin.Y + chunkH);
            float b11 = SampleCorner(origin.X + chunkW, origin.Y + chunkH);

            var (boundaryMax, boundarySecondary) = ResolveBoundaryCrossfade(
                provider, terrain, origin, width, height, tileSize);
            if (boundaryMax > maxBlend)
            {
                maxBlend = boundaryMax;
                bestSecondary = boundarySecondary;
            }

            return (b00, b10, b01, b11, maxBlend, bestSecondary);
        }
    }
}
