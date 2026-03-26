using System.Numerics;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Probes terrain under camera look direction and stores the dig target.
    /// </summary>
    public class DigProbeSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;

        public DigProbeSystem(EntityRegistry entities, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<CameraComponent, DigInteractionComponent>())
            {
                var cam = entity.GetComponent<CameraComponent>();
                var dig = entity.GetComponent<DigInteractionComponent>();

                dig.HasGroundHit = TryGetGroundHit(
                    cam,
                    dig.ProbeMaxDistance,
                    dig.ProbeStep,
                    dig.ProbeEpsilon,
                    out var hit);
                dig.GroundHit = hit;
                entity.SetComponent(dig);
            }
        }

        private bool TryGetGroundHit(CameraComponent cam, float maxDistance, float step, float epsilon, out Vector3 hit)
        {
            hit = default;
            var dir = Vector3.Normalize(cam.Target - cam.Position);

            float downDot = Vector3.Dot(dir, Vector3.UnitY);
            if (downDot > -0.15f)
                return false;

            float traveled = 0f;
            var p = cam.Position;
            while (traveled <= maxDistance)
            {
                p += dir * step;
                traveled += step;

                float groundY = _terrain.SampleHeight(new Vector3(p.X, 0, p.Z));
                if (p.Y <= groundY + epsilon)
                {
                    hit = new Vector3(p.X, groundY, p.Z);
                    return true;
                }
            }

            return false;
        }
    }
}

