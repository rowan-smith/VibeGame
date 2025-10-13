using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes
{
    /// <summary>
    /// Helper methods to compute dominant biome over an area in a consistent way across systems.
    /// </summary>
    public static class BiomeSampling
    {
        /// <summary>
        /// Samples a grid of points over the chunk area and returns the most frequent biome.
        /// Optionally applies extra weight to the center sample to stabilize near boundaries.
        /// </summary>
        /// <param name="provider">Biome provider</param>
        /// <param name="terrain">Terrain generator (can be null if provider ignores it)</param>
        /// <param name="chunkOriginWorld">World origin of the chunk (minX, minZ)</param>
        /// <param name="chunkSize">Chunk size in tiles (not including +1 seam)</param>
        /// <param name="tileSize">Tile size in world units</param>
        /// <param name="samplesPerAxis">Grid resolution (e.g., 9 for 9x9). Minimum 3.</param>
        /// <param name="centerExtraWeight">Additional weight for center sample (e.g., 2.0 adds two extra votes)</param>
        public static IBiome GetDominantBiomeForArea(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 chunkOriginWorld,
            int chunkSize,
            float tileSize,
            int samplesPerAxis = 9,
            float centerExtraWeight = 2f)
        {
            var (primary, _) = GetDominantAndSecondaryBiomeForArea(provider, terrain, chunkOriginWorld, chunkSize, tileSize, samplesPerAxis, centerExtraWeight);
            return primary;
        }

        public static (IBiome primary, IBiome? secondary) GetDominantAndSecondaryBiomeForArea(
            IBiomeProvider provider,
            ITerrainGenerator? terrain,
            Vector2 chunkOriginWorld,
            int chunkSize,
            float tileSize,
            int samplesPerAxis = 9,
            float centerExtraWeight = 2f)
        {
            samplesPerAxis = Math.Max(3, samplesPerAxis);
            var counts = new Dictionary<string, (IBiome biome, float weight)>(StringComparer.OrdinalIgnoreCase);

            float areaWorldSize = chunkSize * tileSize;
            float step = areaWorldSize / (samplesPerAxis + 1);

            int centerIdx = (samplesPerAxis + 1) / 2; // integer center index in 1..samplesPerAxis

            for (int j = 1; j <= samplesPerAxis; j++)
            {
                for (int i = 1; i <= samplesPerAxis; i++)
                {
                    float wx = chunkOriginWorld.X + i * step;
                    float wz = chunkOriginWorld.Y + j * step;
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
                float cx = chunkOriginWorld.X + areaWorldSize * 0.5f;
                float cz = chunkOriginWorld.Y + areaWorldSize * 0.5f;
                var c = provider.GetBiomeAt(new Vector2(cx, cz), terrain!);
                return (c, null);
            }

            var ordered = counts.Values.OrderByDescending(v => v.weight).ToList();
            var primary = ordered[0].biome;
            IBiome? secondary = ordered.Count > 1 ? ordered[1].biome : null;
            return (primary, secondary);
        }
    }
}
