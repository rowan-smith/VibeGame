using Veilborne.Core.Terrain;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Pumps asynchronous terrain generation/install queues each frame.
    /// </summary>
    public class TerrainGenSystem : ISystem
    {
        private readonly TerrainManager _terrainManager;
        private float _accumulatedSeconds;
        private Task? _pumpTask;
        private const float PumpTickSeconds = 1f / 60f;

        public TerrainGenSystem(TerrainManager terrainManager)
        {
            _terrainManager = terrainManager;
        }

        public void Update(float dt)
        {
            if (_pumpTask is { IsCompleted: false })
                return;

            if (_pumpTask is { IsFaulted: true })
                _ = _pumpTask.Exception;

            _pumpTask = null;
            _accumulatedSeconds += MathF.Max(0f, dt);
            if (_accumulatedSeconds < PumpTickSeconds)
                return;
            _accumulatedSeconds = 0f;
            _pumpTask = _terrainManager.PumpAsyncJobs();
        }
    }
}
