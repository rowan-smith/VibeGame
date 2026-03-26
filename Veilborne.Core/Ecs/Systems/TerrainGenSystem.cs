using Veilborne.Terrain;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Pumps asynchronous terrain generation/install queues each frame.
    /// </summary>
    public class TerrainGenSystem : ISystem
    {
        private readonly TerrainManager _terrainManager;
        private float _accumulatedSeconds;
        private const float PumpTickSeconds = 1f / 30f;

        public TerrainGenSystem(TerrainManager terrainManager)
        {
            _terrainManager = terrainManager;
        }

        public void Update(float dt)
        {
            _accumulatedSeconds += MathF.Max(0f, dt);
            if (_accumulatedSeconds < PumpTickSeconds)
                return;
            _accumulatedSeconds = 0f;
            _terrainManager.PumpAsyncJobs().GetAwaiter().GetResult();
        }
    }
}
