using Veilborne.Terrain;

namespace Veilborne.Core.Tests.Unit;

public class TerrainBiomeBlendPolicyRegressionTests
{
    [Fact]
    public void ShouldEnqueueBiomeMergeBuild_only_before_first_evaluated_mesh()
    {
        Assert.True(TerrainChunkBiomeBlendPolicy.ShouldEnqueueBiomeMergeBuild(true, biomeMergeEvaluated: false));
        Assert.False(TerrainChunkBiomeBlendPolicy.ShouldEnqueueBiomeMergeBuild(true, biomeMergeEvaluated: true));
        Assert.False(TerrainChunkBiomeBlendPolicy.ShouldEnqueueBiomeMergeBuild(false, biomeMergeEvaluated: false));
    }

    [Fact]
    public void ShouldEnqueueBiomeMergeBuild_does_not_requeue_evaluated_chunks_with_narrow_blend_strip()
    {
        // Regression: low coverage + high cached merge used to re-enqueue every frame (~3 FPS).
        Assert.False(TerrainChunkBiomeBlendPolicy.ShouldEnqueueBiomeMergeBuild(
            crossfadeEnabled: true,
            biomeMergeEvaluated: true));
    }

    [Fact]
    public void ShouldBuildMergeBlendMap_when_merge_biome_exists_regardless_of_boundary_strength()
    {
        Assert.True(TerrainChunkBiomeBlendPolicy.ShouldBuildMergeBlendMap("neighbor_biome"));
        Assert.False(TerrainChunkBiomeBlendPolicy.ShouldBuildMergeBlendMap(null));
        Assert.False(TerrainChunkBiomeBlendPolicy.ShouldBuildMergeBlendMap(""));
    }

    [Fact]
    public void ChunkNeedsBiomeMergeDraw_allows_narrow_edge_strips_without_coverage_gate()
    {
        // Regression: BiomeBlendCoverage > 2% gate skipped merge draws at chunk edges.
        Assert.True(TerrainChunkBiomeBlendPolicy.ChunkNeedsBiomeMergeDraw(
            hasMergeTexture: true,
            storedMergeBiomeId: "biome_b",
            cachedMaxMerge: 0.4f,
            biomeBlendCoverage: 0.01f));

        Assert.True(TerrainChunkBiomeBlendPolicy.ChunkNeedsBiomeMergeDraw(
            hasMergeTexture: true,
            storedMergeBiomeId: "",
            cachedMaxMerge: 0.5f,
            biomeBlendCoverage: 0.001f));
    }

    [Fact]
    public void ChunkNeedsBiomeMergeDraw_requires_merge_texture()
    {
        Assert.False(TerrainChunkBiomeBlendPolicy.ChunkNeedsBiomeMergeDraw(
            hasMergeTexture: false,
            storedMergeBiomeId: "biome_b",
            cachedMaxMerge: 0.5f,
            biomeBlendCoverage: 0.5f));
    }

    [Fact]
    public void ShouldBindMergeTexture_when_stored_merge_or_cached_max_present()
    {
        Assert.True(TerrainChunkBiomeBlendPolicy.ShouldBindMergeTexture(
            crossfadeEnabled: true,
            hasMergeBiome: true,
            storedMergeBiomeId: "biome_b",
            maxMerge: 0f,
            cachedMaxMerge: 0f));

        Assert.True(TerrainChunkBiomeBlendPolicy.ShouldBindMergeTexture(
            crossfadeEnabled: true,
            hasMergeBiome: true,
            storedMergeBiomeId: "",
            maxMerge: 0.5f,
            cachedMaxMerge: 0f));

        Assert.False(TerrainChunkBiomeBlendPolicy.ShouldBindMergeTexture(
            crossfadeEnabled: false,
            hasMergeBiome: true,
            storedMergeBiomeId: "biome_b",
            maxMerge: 1f,
            cachedMaxMerge: 1f));
    }

    [Fact]
    public void ChunkVisualsMatch_uses_stored_ids_after_mesh_upload_clears_bound_ids()
    {
        var chunk = new TerrainChunkVisualState
        {
            HasPrimaryTexture = true,
            BiomeId = "",
            MergeBiomeId = "",
            StoredPrimaryBiomeId = "aether",
            StoredMergeBiomeId = "steppe",
            UseSplatLayering = true,
            LayerMode = 1
        };

        Assert.True(TerrainChunkBiomeBlendPolicy.ChunkVisualsMatch(
            chunk, "aether", "steppe", useSplatLayering: true, layerMode: 1));
    }

    [Fact]
    public void ChunkVisualsMatch_requires_primary_texture_to_avoid_flat_gray_skip()
    {
        // Regression: visualsMatch passed while PrimaryTexture was null after upload.
        var chunk = new TerrainChunkVisualState
        {
            HasPrimaryTexture = false,
            StoredPrimaryBiomeId = "aether",
            StoredMergeBiomeId = "steppe"
        };

        Assert.False(TerrainChunkBiomeBlendPolicy.ChunkVisualsMatch(
            chunk, "aether", "steppe", useSplatLayering: false, layerMode: 0));
    }

    [Fact]
    public void ResolveSplatPrimaryBiomeId_prefers_pair_primary_over_generation_center()
    {
        Assert.Equal("pair_primary", TerrainChunkBiomeBlendPolicy.ResolveSplatPrimaryBiomeId("pair_primary", "center_biome"));
        Assert.Equal("center_biome", TerrainChunkBiomeBlendPolicy.ResolveSplatPrimaryBiomeId("", "center_biome"));
    }

    [Fact]
    public void ResolveBiomeBlendSampleStride_uses_coarse_stride_on_lod_grids()
    {
        Assert.Equal(4, TerrainChunkBiomeBlendPolicy.ResolveBiomeBlendSampleStride(
            129, 129, fastMeshBuild: false, maxBoundaryBlend: 0.5f));
        Assert.Equal(1, TerrainChunkBiomeBlendPolicy.ResolveBiomeBlendSampleStride(
            33, 33, fastMeshBuild: false, maxBoundaryBlend: 0.5f));
    }

    [Fact]
    public void AllowSyncMeshBuild_rejects_lod_vertex_counts()
    {
        Assert.True(TerrainChunkBiomeBlendPolicy.AllowSyncMeshBuild(65 * 65));
        Assert.False(TerrainChunkBiomeBlendPolicy.AllowSyncMeshBuild(129 * 129));
    }
}
