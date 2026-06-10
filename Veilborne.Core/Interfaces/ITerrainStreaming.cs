using System.Numerics;
using Veilborne.Terrain;

namespace Veilborne.Interfaces
{
    /// <summary>
    /// Async terrain streaming operations used by the engine and ECS terrain systems.
    /// </summary>
    public interface ITerrainStreaming
    {
        void SetWarmupMode(bool enabled);

        void UpdateAround(Vector3 worldPos, int queueRadiusHint);

        Task PumpAsyncJobs();

        void ProcessPendingMeshBuilds();

        TerrainLoadingProgress GetLoadingProgress();
    }
}
