namespace Veilborne.Terrain
{
    /// <summary>
    /// Pure policy helpers for terrain biome merge/build/draw decisions.
    /// Kept in Core so regressions can be covered by unit tests without GPU dependencies.
    /// </summary>
    public static class TerrainChunkBiomeBlendPolicy
    {
        public const float BiomeMergeCornerThreshold = 0.015f;

        /// <summary>65×65 vertices — larger LOD meshes must build off the render thread.</summary>
        public const int MaxSyncMeshVertices = 65 * 65;

        public static bool AllowSyncMeshBuild(int vertexCount) =>
            vertexCount > 0 && vertexCount <= MaxSyncMeshVertices;

        /// <summary>
        /// Coarser stride on large grids keeps horizon LOD builds from stalling the frame.
        /// </summary>
        public static int ResolveBiomeBlendSampleStride(
            int gridWidth,
            int gridHeight,
            bool fastMeshBuild,
            float maxBoundaryBlend)
        {
            int grid = Math.Max(gridWidth, gridHeight);
            if (fastMeshBuild)
                return grid >= 97 ? 8 : grid >= 49 ? 4 : 4;

            if (grid >= 97)
                return maxBoundaryBlend > BiomeMergeCornerThreshold ? 4 : 8;
            if (grid >= 49)
                return maxBoundaryBlend > BiomeMergeCornerThreshold ? 2 : 4;
            return maxBoundaryBlend > BiomeMergeCornerThreshold ? 1 : 2;
        }

        /// <summary>
        /// Only the initial async mesh build for biome vertex weights should be queued.
        /// Do not add follow-up enqueue rules based on low coverage — that caused per-frame
        /// mesh rebuilds and ~3 FPS (GPU SetData every frame).
        /// </summary>
        public static bool ShouldEnqueueBiomeMergeBuild(bool crossfadeEnabled, bool biomeMergeEvaluated) =>
            crossfadeEnabled && !biomeMergeEvaluated;

        /// <summary>
        /// Build per-vertex merge weights whenever a merge biome id is known.
        /// Do not gate on boundary strength — narrow edge strips still need weights.
        /// </summary>
        public static bool ShouldBuildMergeBlendMap(string? mergeBiomeId) =>
            !string.IsNullOrEmpty(mergeBiomeId);

        public static bool ShouldBindMergeTexture(
            bool crossfadeEnabled,
            bool hasMergeBiome,
            string? storedMergeBiomeId,
            float maxMerge,
            float cachedMaxMerge) =>
            crossfadeEnabled &&
            hasMergeBiome &&
            (!string.IsNullOrEmpty(storedMergeBiomeId) ||
             maxMerge > BiomeMergeCornerThreshold ||
             cachedMaxMerge > BiomeMergeCornerThreshold);

        /// <summary>
        /// Merge draws must not require a minimum fraction of blended vertices.
        /// A single transition column at a chunk edge is valid and common.
        /// </summary>
        public static bool ChunkNeedsBiomeMergeDraw(
            bool hasMergeTexture,
            string? storedMergeBiomeId,
            float cachedMaxMerge,
            float biomeBlendCoverage) =>
            hasMergeTexture &&
            (!string.IsNullOrEmpty(storedMergeBiomeId) ||
             cachedMaxMerge > BiomeMergeCornerThreshold) &&
            biomeBlendCoverage >= 0f;

        public static bool ChunkVisualsMatch(
            in TerrainChunkVisualState chunk,
            string? activePrimaryId,
            string? activeMergeId,
            bool useSplatLayering,
            byte layerMode)
        {
            if (!chunk.HasPrimaryTexture)
                return false;

            string boundPrimary = !string.IsNullOrEmpty(chunk.BiomeId)
                ? chunk.BiomeId
                : chunk.StoredPrimaryBiomeId;
            string boundMerge = !string.IsNullOrEmpty(chunk.MergeBiomeId)
                ? chunk.MergeBiomeId
                : chunk.StoredMergeBiomeId;
            return string.Equals(boundPrimary, activePrimaryId ?? string.Empty, StringComparison.Ordinal) &&
                   string.Equals(boundMerge, activeMergeId ?? string.Empty, StringComparison.Ordinal) &&
                   chunk.UseSplatLayering == useSplatLayering &&
                   (!useSplatLayering || chunk.LayerMode == layerMode);
        }

        public static string ResolveSplatPrimaryBiomeId(string pairPrimaryId, string generationBiomeId) =>
            !string.IsNullOrEmpty(pairPrimaryId) ? pairPrimaryId : generationBiomeId;
    }

    public readonly struct TerrainChunkVisualState
    {
        public bool HasPrimaryTexture { get; init; }
        public string BiomeId { get; init; }
        public string MergeBiomeId { get; init; }
        public string StoredPrimaryBiomeId { get; init; }
        public string StoredMergeBiomeId { get; init; }
        public bool UseSplatLayering { get; init; }
        public byte LayerMode { get; init; }
    }
}
