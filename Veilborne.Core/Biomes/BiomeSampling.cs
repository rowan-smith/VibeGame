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
                blend = Math.Clamp(secondaryWeight / denom, 0f, 0.49f);

            return (primary, secondary, blend);
        }

        /// <summary>
        /// Resolves the dominant biome (and optional secondary blend) for a terrain chunk
        /// by sampling across its full area. Keeps biome assignment stable across LOD rings.
        /// </summary>
        public static (IBiome primary, IBiome? secondary, float secondaryBlend) ResolveChunkBiomeBlend(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 chunkOriginWorld,
            int chunkSize,
            float tileSize,
            int samplesPerAxis = 0,
            float centerExtraWeight = 2f,
            float expandWorldMargin = 0f)
        {
            if (samplesPerAxis <= 0)
            {
                float worldSize = chunkSize * tileSize;
                samplesPerAxis = worldSize >= 384f ? 11 : worldSize >= 128f ? 9 : 7;
            }

            var (primary, secondary, primaryWeight, secondaryWeight) = GetDominantAndSecondaryBiomeForAreaWithWeights(
                provider,
                terrain,
                chunkOriginWorld,
                chunkSize,
                tileSize,
                samplesPerAxis,
                centerExtraWeight,
                expandWorldMargin);

            if (secondary is null || string.Equals(secondary.Id, primary.Id, StringComparison.OrdinalIgnoreCase))
                return (primary, null, 0f);

            float denom = primaryWeight + secondaryWeight;
            float blend = denom > 1e-5f ? Math.Clamp(secondaryWeight / denom, 0f, 0.49f) : 0f;
            return blend > 0.001f ? (primary, secondary, blend) : (primary, null, 0f);
        }

        /// <summary>
        /// Biome used for terrain surface color. Fine rings sample the full chunk area;
        /// coarse LOD rings use the chunk center so a visible hillside isn't painted by
        /// a different biome covering most of a 128–512 m tile.
        /// </summary>
        public static (IBiome primary, IBiome? secondary, float secondaryBlend) ResolveVisualBiomeForChunk(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 chunkOriginWorld,
            int chunkSize,
            float tileSize)
        {
            float worldSize = chunkSize * tileSize;
            if (tileSize <= 1.5f)
                return ResolveChunkBiomeBlend(provider, terrain, chunkOriginWorld, chunkSize, tileSize);

            Vector2 center = new Vector2(
                chunkOriginWorld.X + worldSize * 0.5f,
                chunkOriginWorld.Y + worldSize * 0.5f);

            if (provider is SimpleBiomeProvider simple)
                return simple.GetBiomeBlendAt(center, terrain!);

            var primary = provider.GetBiomeAt(center, terrain!);
            return (primary, null, 0f);
        }
    }
}
