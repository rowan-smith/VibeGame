using System.Numerics;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Computes a simple near/far ordering hint used by object rendering.
    /// </summary>
    public class SortSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly EcsFrameContext _frameContext;
        private readonly List<(Entity Entity, float DistanceSq)> _sortEntries = new();

        public SortSystem(EntityRegistry entities, EcsFrameContext frameContext)
        {
            _entities = entities;
            _frameContext = frameContext;
        }

        public void Update(float dt)
        {
            if (!_frameContext.HasPrimaryCamera)
                return;

            var cameraPos = _frameContext.PrimaryCameraPosition;
            _sortEntries.Clear();

            _entities.ForEachWith<RenderComponent, TransformComponent>((Entity entity, ref RenderComponent render, ref TransformComponent transform) =>
            {
                if (!render.Visible)
                    return;

                _sortEntries.Add((entity, Vector3.DistanceSquared(transform.Position, cameraPos)));
            });

            _sortEntries.Sort(static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
            _frameContext.WasSortedThisFrame = _sortEntries.Count > 0;
        }
    }
}
