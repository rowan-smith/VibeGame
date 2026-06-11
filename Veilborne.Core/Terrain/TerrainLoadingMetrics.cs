namespace Veilborne.Terrain
{
    /// <summary>
    /// Maps terrain streaming counters to a 0-1 loading bar value.
    /// Installed chunks drive completion; pipeline and LOD keep the bar moving during tail waits.
    /// </summary>
    public static class TerrainLoadingMetrics
    {
        public static float ComputeProgress01(
            int desiredPlayable,
            int loadedPlayable,
            int generatingPlayable,
            int desiredLod,
            int loadedLod,
            int pendingSpawnObjects)
        {
            if (desiredPlayable <= 0)
                return 1f;

            float installedRatio = Math.Clamp(loadedPlayable / (float)desiredPlayable, 0f, 1f);
            float pipelineRatio = Math.Clamp(
                (loadedPlayable + generatingPlayable) / (float)desiredPlayable,
                0f,
                1f);
            float lodRatio = desiredLod > 0
                ? Math.Clamp(loadedLod / (float)desiredLod, 0f, 1f)
                : 1f;
            float spawnRatio = pendingSpawnObjects <= 0 ? 1f : 0.85f;

            float progress = installedRatio * 0.70f
                + pipelineRatio * 0.10f
                + lodRatio * 0.12f
                + spawnRatio * 0.08f;

            bool playableReady = generatingPlayable == 0 && loadedPlayable >= desiredPlayable;
            if (playableReady && pendingSpawnObjects == 0)
                return 1f;

            return Math.Clamp(progress, 0f, 0.995f);
        }
    }
}
