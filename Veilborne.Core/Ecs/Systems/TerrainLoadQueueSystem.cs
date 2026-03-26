using System.Numerics;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core;
using Veilborne.Terrain;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Prepares terrain load priorities around the active camera.
    /// </summary>
    public class TerrainLoadQueueSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly TerrainManager _terrainManager;
        private float _accumulatedSeconds;
        private const float StreamingTickSeconds = 1f / 15f;
        private const float DigStreamingTickSeconds = 1f / 30f;
        private bool _wasDigActive;

        public TerrainLoadQueueSystem(EntityRegistry entities, TerrainManager terrainManager)
        {
            _entities = entities;
            _terrainManager = terrainManager;
        }

        public void Update(float dt)
        {
            Vector3 cameraPos = Vector3.Zero;
            bool hasCamera = false;
            foreach (var entity in _entities.GetEntitiesWith<CameraComponent>())
            {
                cameraPos = entity.GetComponent<CameraComponent>().Position;
                hasCamera = true;
                break;
            }

            if (!hasCamera)
                return;

            _accumulatedSeconds += MathF.Max(0f, dt);
            bool digActive = false;
            foreach (var entity in _entities.GetEntitiesWith<DigInteractionComponent>())
            {
                var dig = entity.GetComponent<DigInteractionComponent>();
                if (dig.IsDigHeld && dig.HasGroundHit)
                {
                    digActive = true;
                    break;
                }
            }
            bool digJustStarted = digActive && !_wasDigActive;
            float tick = digActive ? DigStreamingTickSeconds : StreamingTickSeconds;
            _wasDigActive = digActive;
            if (!digJustStarted && _accumulatedSeconds < tick)
                return;

            _accumulatedSeconds = 0f;
            _terrainManager.UpdateAround(cameraPos, 0);
        }
    }
}

