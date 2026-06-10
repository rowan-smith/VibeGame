using System.Numerics;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Prepares terrain load priorities around the active camera.
    /// </summary>
    public class TerrainLoadQueueSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly ITerrainStreaming _terrainStreaming;
        private readonly IWorldConfigService _config;
        private float _accumulatedSeconds;
        private Vector3 _lastCameraPos;
        private bool _hasLastCameraPos;
        private const float StreamingTickSeconds = 1f / 10f;
        private const float MovingStreamingTickSeconds = 1f / 8f;
        private const float DigStreamingTickSeconds = 1f / 12f;
        private bool _wasDigActive;

        public TerrainLoadQueueSystem(EntityRegistry entities, ITerrainStreaming terrainStreaming, IWorldConfigService config)
        {
            _entities = entities;
            _terrainStreaming = terrainStreaming;
            _config = config;
        }

        public void Update(float dt)
        {
            Vector3 cameraPos = Vector3.Zero;
            bool hasCamera = false;
            _entities.ForEachWith<CameraComponent>(entity =>
            {
                if (hasCamera) return;
                cameraPos = entity.GetComponent<CameraComponent>().Position;
                hasCamera = true;
            });

            if (!hasCamera)
                return;

            float speedMps = 0f;
            if (_hasLastCameraPos && dt > 1e-5f)
                speedMps = Vector3.Distance(cameraPos, _lastCameraPos) / dt;
            _lastCameraPos = cameraPos;
            _hasLastCameraPos = true;

            _accumulatedSeconds += MathF.Max(0f, dt);
            bool digActive = false;
            _entities.ForEachWith<DigInteractionComponent>(entity =>
            {
                if (digActive) return;
                var dig = entity.GetComponent<DigInteractionComponent>();
                if (dig.IsDigHeld && dig.HasGroundHit)
                    digActive = true;
            });
            bool digJustStarted = digActive && !_wasDigActive;
            float tick = digActive
                ? DigStreamingTickSeconds
                : (speedMps > 2.5f ? MovingStreamingTickSeconds : StreamingTickSeconds);
            _wasDigActive = digActive;
            if (!digJustStarted && _accumulatedSeconds < tick)
                return;

            _accumulatedSeconds = 0f;
            int queueRadius = Math.Max(0, _config.Config.TerrainLoadQueueRadius);
            _terrainStreaming.UpdateAround(cameraPos, queueRadius);
        }
    }
}
