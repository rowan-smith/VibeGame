using System.Numerics;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Probes terrain under camera look direction and stores the dig target.
    /// Throttled to run every other frame to reduce SampleHeight overhead.
    /// </summary>
    public class DigProbeSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInfiniteTerrain _terrain;
        private int _frameCounter;

        public DigProbeSystem(EntityRegistry entities, IInfiniteTerrain terrain)
        {
            _entities = entities;
            _terrain = terrain;
        }

        public void Update(float dt)
        {
            _frameCounter++;
            bool isProbeFrame = (_frameCounter & 1) == 0; // every other frame

            _entities.ForEachWith<CameraComponent, DigInteractionComponent>(entity =>
            {
                var dig = entity.GetComponent<DigInteractionComponent>();

                // Skip probe on odd frames unless actively digging
                if (!isProbeFrame && !dig.IsDigHeld)
                    return;

                var cam = entity.GetComponent<CameraComponent>();

                // Use coarser step when not actively digging for cheaper probing
                float step = dig.IsDigHeld ? dig.ProbeStep : dig.ProbeStep * 2f;

                dig.HasGroundHit = TryGetGroundHit(
                    cam,
                    dig.ProbeMaxDistance,
                    step,
                    dig.ProbeEpsilon,
                    out var hit);
                dig.GroundHit = hit;
                entity.SetComponent(dig);
            });
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

