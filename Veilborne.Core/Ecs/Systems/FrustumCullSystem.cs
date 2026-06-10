using System.Numerics;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Performs cheap distance culling and writes per-entity visibility flags.
    /// </summary>
    public class FrustumCullSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly EcsFrameContext _frameContext;

        public FrustumCullSystem(EntityRegistry entities, EcsFrameContext frameContext)
        {
            _entities = entities;
            _frameContext = frameContext;
        }

        public void Update(float dt)
        {
            if (!_frameContext.HasPrimaryCamera)
                return;

            var cameraPos = _frameContext.PrimaryCameraPosition;
            const float maxVisibleDistance = 120f;
            const float maxVisibleDistanceSq = maxVisibleDistance * maxVisibleDistance;

            _entities.ForEachWith<RenderComponent, TransformComponent>((Entity entity, ref RenderComponent render, ref TransformComponent transform) =>
            {
                var distSq = Vector3.DistanceSquared(transform.Position, cameraPos);
                render.Visible = distSq <= maxVisibleDistanceSq;
            });
        }
    }
}
