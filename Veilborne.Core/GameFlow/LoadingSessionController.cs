using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.UI;

namespace Veilborne.GameFlow
{
    /// <summary>
    /// Tracks terrain warmup progress while the loading screen is visible.
    /// Gameplay starts once playable rings (editable + read-only) are ready; LOD streams afterward.
    /// </summary>
    public sealed class LoadingSessionController
    {
        public const double CompletionDelaySeconds = 0.10;

        public float Progress { get; private set; }
        public string StageText { get; private set; } = "Preparing world";
        public int LoadedChunks { get; private set; }
        public int DesiredChunks { get; private set; }
        public int GeneratingChunks { get; private set; }
        public int LoadedEntities { get; private set; }
        public int PendingSpawnObjects { get; private set; }
        public Task? PumpTask { get; private set; }
        public double CompleteTime { get; private set; }

        public void Reset()
        {
            Progress = 0f;
            StageText = "Preparing world";
            LoadedChunks = 0;
            DesiredChunks = 0;
            GeneratingChunks = 0;
            LoadedEntities = 0;
            PendingSpawnObjects = 0;
            PumpTask = null;
            CompleteTime = 0;
        }

        public void BeginWarmup(ITerrainStreaming terrainStreaming)
        {
            Reset();
            terrainStreaming.SetWarmupMode(true);
        }

        public void CancelWarmup(ITerrainStreaming terrainStreaming)
        {
            terrainStreaming.SetWarmupMode(false);
            Reset();
        }

        public void FinishWarmup(ITerrainStreaming terrainStreaming)
        {
            terrainStreaming.SetWarmupMode(false);
            Reset();
        }

        /// <summary>
        /// Pumps terrain streaming while loading. Returns true when the session is ready to enter gameplay.
        /// </summary>
        public bool Update(float dt, ITerrainStreaming terrainStreaming, Vector3 cameraPosition)
        {
            terrainStreaming.SetWarmupMode(true);
            terrainStreaming.UpdateAround(cameraPosition, 0);
            terrainStreaming.ProcessPendingMeshBuilds();

            // Drain async install queues aggressively during warmup (one pump/frame stalls at ~94%).
            const int maxPumpRounds = 8;
            for (int round = 0; round < maxPumpRounds; round++)
            {
                if (PumpTask is { IsCompleted: false })
                    break;
                if (PumpTask is { IsFaulted: true })
                    _ = PumpTask.Exception;

                int loadedBefore = terrainStreaming.GetLoadingProgress().LoadedChunks;
                PumpTask = terrainStreaming.PumpAsyncJobs();
                int loadedAfter = terrainStreaming.GetLoadingProgress().LoadedChunks;
                if (loadedBefore == loadedAfter && round > 0)
                    break;
            }

            var loading = terrainStreaming.GetLoadingProgress();
            Progress = MathF.Max(Progress, loading.Progress01);
            StageText = loading.Stage;
            DesiredChunks = loading.DesiredChunks;
            LoadedChunks = loading.LoadedChunks;
            GeneratingChunks = loading.GeneratingChunks;
            LoadedEntities = loading.LoadedEntities;
            PendingSpawnObjects = loading.PendingSpawnObjects;

            bool loadingReady = Progress >= 0.999f &&
                                GeneratingChunks == 0 &&
                                LoadedChunks >= DesiredChunks &&
                                PendingSpawnObjects == 0;
            if (loadingReady)
                CompleteTime += dt;
            else
                CompleteTime = 0;

            return CompleteTime >= CompletionDelaySeconds;
        }

        public LoadingScreenData ToScreenData(ITerrainStreaming terrainStreaming)
        {
            var loading = terrainStreaming.GetLoadingProgress();
            return new(
                Progress,
                StageText,
                LoadedChunks,
                DesiredChunks,
                GeneratingChunks,
                LoadedEntities,
                loading.LoadedBackgroundChunks,
                loading.DesiredBackgroundChunks);
        }
    }
}
