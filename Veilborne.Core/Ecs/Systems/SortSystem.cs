using System.Numerics;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Computes a simple near/far ordering hint used by object rendering.
    /// </summary>
    public class SortSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly RenderFrameState _renderFrameState;

        public SortSystem(EntityRegistry entities, RenderFrameState renderFrameState)
        {
            _entities = entities;
            _renderFrameState = renderFrameState;
        }

        public void Update(float dt)
        {
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

            var sortEntries = new List<(Entity Entity, float DistanceSq)>();
            foreach (var entity in _entities.GetEntitiesWith<RenderComponent, TransformComponent>())
            {
                var render = entity.GetComponent<RenderComponent>();
                if (!render.Visible)
                    continue;

                var transform = entity.GetComponent<TransformComponent>();
                var distSq = Vector3.DistanceSquared(transform.Position, cameraPos);
                sortEntries.Add((entity, distSq));
            }

            sortEntries.Sort((a, b) => a.DistanceSq.CompareTo(b.DistanceSq));

            _renderFrameState.WasSortedThisFrame = sortEntries.Count > 0;
        }
    }
}
