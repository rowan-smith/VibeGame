using System.Numerics;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Performs cheap distance culling and writes per-entity visibility flags.
    /// </summary>
    public class FrustumCullSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly RenderFrameState _renderFrameState;

        public FrustumCullSystem(EntityRegistry entities, RenderFrameState renderFrameState)
        {
            _entities = entities;
            _renderFrameState = renderFrameState;
        }

        public void Update(float dt)
        {
            _renderFrameState.WasSortedThisFrame = false;

            Vector3 cameraPos = Vector3.Zero;
            bool hasCamera = false;
            foreach (var cameraEntity in _entities.GetEntitiesWith<CameraComponent>())
            {
                cameraPos = cameraEntity.GetComponent<CameraComponent>().Position;
                hasCamera = true;
                break;
            }

            if (!hasCamera)
                return;

            const float maxVisibleDistance = 120f;
            const float maxVisibleDistanceSq = maxVisibleDistance * maxVisibleDistance;

            foreach (var entity in _entities.GetEntitiesWith<RenderComponent, TransformComponent>())
            {
                var render = entity.GetComponent<RenderComponent>();
                var transform = entity.GetComponent<TransformComponent>();
                var distSq = Vector3.DistanceSquared(transform.Position, cameraPos);
                var shouldBeVisible = distSq <= maxVisibleDistanceSq;
                if (render.Visible != shouldBeVisible)
                {
                    render.Visible = shouldBeVisible;
                    entity.SetComponent(render);
                }
            }
        }
    }
}
