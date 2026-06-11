using System.Numerics;
using Veilborne.Biomes;
using Veilborne.Core.Tests.Helpers;
using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Unit;

public class BiomeSamplingRegressionTests
{
    [Fact]
    public void BuildChunkPairBlendMap_produces_complementary_alphas_for_swapped_pairs()
    {
        var provider = BiomeTestFactory.CreateProvider(
            seed: 4242,
            cellSize: 160f,
            blendWidth: 140f,
            ("desert", 0.9f, 0.1f),
            ("forest", 0.2f, 0.9f),
            ("tundra", 0.1f, 0.2f));

        var (worldPos, primaryId, mergeId, _) = BiomeTestFactory.FindTransitionSample(provider);

        var (mapPrimaryMerge, _) = BiomeSampling.BuildChunkPairBlendMap(
            provider, null, worldPos, 3, 3, 2f, primaryId, mergeId, sampleStride: 1);
        var (mapMergePrimary, _) = BiomeSampling.BuildChunkPairBlendMap(
            provider, null, worldPos, 3, 3, 2f, mergeId, primaryId, sampleStride: 1);

        float a = mapPrimaryMerge[0, 0];
        float b = mapMergePrimary[0, 0];
        Assert.InRange(a, 0.05f, 0.95f);
        Assert.InRange(b, 0.05f, 0.95f);
        // PerturbMergeWeight adds organic noise; swapped pairs should stay roughly complementary.
        Assert.InRange(a + b, 0.75f, 1.25f);
        Assert.InRange(MathF.Abs(a - (1f - b)), 0f, 0.35f);
    }

    [Fact]
    public void ResolveChunkBiomePair_finds_merge_biome_near_transition()
    {
        var provider = BiomeTestFactory.CreateProvider(
            seed: 777,
            cellSize: 140f,
            blendWidth: 120f,
            ("alpha", 0.85f, 0.15f),
            ("beta", 0.15f, 0.85f));

        var (worldPos, primaryId, mergeId, _) = BiomeTestFactory.FindTransitionSample(provider);
        var origin = new Vector2(worldPos.X - 64f, worldPos.Y - 64f);

        var (pairPrimary, pairMerge, maxMerge) = BiomeSampling.ResolveChunkBiomePair(
            provider, null, origin, gridWidth: 65, gridHeight: 65, tileSize: 2f, expandMarginTiles: 4f);

        Assert.False(string.IsNullOrEmpty(pairMerge));
        Assert.NotEqual(pairPrimary, pairMerge);
        Assert.True(maxMerge > TerrainChunkBiomeBlendPolicy.BiomeMergeCornerThreshold);
        Assert.True(primaryId == pairPrimary || mergeId == pairPrimary || primaryId == pairMerge || mergeId == pairMerge);
    }

    [Fact]
    public void BuildChunkPairBlendMap_has_nonzero_weights_along_chunk_edge_when_merge_assigned()
    {
        var provider = BiomeTestFactory.CreateProvider(
            seed: 9001,
            cellSize: 150f,
            blendWidth: 130f,
            ("moor", 0.8f, 0.3f),
            ("marsh", 0.3f, 0.8f));

        var (worldPos, primaryId, mergeId, _) = BiomeTestFactory.FindTransitionSample(provider);
        var origin = new Vector2(worldPos.X - 126f, worldPos.Y - 10f);

        var (map, maxMerge) = BiomeSampling.BuildChunkPairBlendMap(
            provider, null, origin, width: 65, height: 65, tileSize: 2f, primaryId, mergeId, sampleStride: 2);

        Assert.True(maxMerge > 0.05f);
        float edgeMax = 0f;
        int xEdge = map.GetLength(0) - 1;
        for (int z = 0; z < map.GetLength(1); z++)
            edgeMax = MathF.Max(edgeMax, map[xEdge, z]);
        Assert.True(edgeMax > 0.02f, "Expected non-zero merge alpha along chunk edge at biome boundary.");
    }

    [Fact]
    public void ResolveBoundaryCrossfade_detects_neighbor_when_edge_local_primary_differs_from_area_primary()
    {
        var provider = BiomeTestFactory.CreateProvider(
            seed: 5150,
            cellSize: 150f,
            blendWidth: 130f,
            ("highland", 0.85f, 0.2f),
            ("lowland", 0.2f, 0.85f),
            ("coast", 0.5f, 0.95f));

        const int grid = 65;
        const float tile = 2f;
        float chunkWorld = (grid - 1) * tile;

        for (float z = -2000f; z <= 2000f; z += 80f)
        for (float x = -2000f; x <= 2000f; x += 80f)
        {
            var origin = new Vector2(x, z);
            var (areaPrimary, _, _, _) = BiomeSampling.GetDominantAndSecondaryBiomeForAreaWithWeights(
                provider, null, origin, grid - 1, tile, 7, 2f, tile * 4f);
            string areaPrimaryId = areaPrimary.Id;

            float edgeX = origin.X + chunkWorld;
            var (edgePrimary, edgeSecondary, edgeBlend) = provider.GetBiomeBlendAt(
                new Vector2(edgeX, origin.Y + chunkWorld * 0.5f), null);
            if (edgeSecondary is null || edgeBlend <= 0.05f)
                continue;
            if (string.Equals(edgePrimary.Id, areaPrimaryId, StringComparison.OrdinalIgnoreCase))
                continue;
            if (!string.Equals(edgeSecondary.Id, areaPrimaryId, StringComparison.OrdinalIgnoreCase))
                continue;

            var (boundaryMax, boundarySecondary) = BiomeSampling.ResolveBoundaryCrossfade(
                provider, null, origin, grid, grid, tile, areaPrimaryId);

            Assert.True(boundaryMax > TerrainChunkBiomeBlendPolicy.BiomeMergeCornerThreshold);
            Assert.NotNull(boundarySecondary);
            Assert.Equal(edgePrimary.Id, boundarySecondary.Id);
            return;
        }

        Assert.Fail("Could not find edge-local-primary mismatch scenario in search area.");
    }
}
