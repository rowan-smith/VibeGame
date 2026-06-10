namespace Veilborne.Terrain
{
    /// <summary>
    /// Loading progress for the warmup screen. Chunk counts reflect playable rings
    /// (editable + read-only). Background LOD counts are informational only.
    /// </summary>
    public readonly record struct TerrainLoadingProgress(
        float Progress01,
        string Stage,
        int DesiredChunks,
        int LoadedChunks,
        int GeneratingChunks,
        int LoadedEntities,
        int PendingSpawnObjects,
        int DesiredBackgroundChunks = 0,
        int LoadedBackgroundChunks = 0);
}
