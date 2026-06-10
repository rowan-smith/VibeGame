namespace Veilborne.Terrain
{
    public readonly record struct TerrainLoadingProgress(
        float Progress01,
        string Stage,
        int DesiredChunks,
        int LoadedChunks,
        int GeneratingChunks,
        int LoadedEntities,
        int PendingSpawnObjects);
}
