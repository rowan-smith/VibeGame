using System.Numerics;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Accumulates force and acceleration from gravity and drag data components.
    /// </summary>
    public class ForceSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public ForceSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<CameraComponent>())
            {
                if (!entity.TryGetComponent<MassComponent>(out var mass) || mass.IsKinematic)
                    continue;
                if (!entity.TryGetComponent<ForceComponent>(out var force))
                    continue;
                if (!entity.TryGetComponent<AccelerationComponent>(out var acceleration))
                    continue;

                if (entity.TryGetComponent<GravityComponent>(out var gravity))
                {
                    force.Value += gravity.Direction * mass.Value;
                }

                if (entity.TryGetComponent<VelocityComponent>(out var velocity) &&
                    entity.TryGetComponent<DragComponent>(out var drag) &&
                    drag.Linear > 0f)
                {
                    force.Value -= velocity.Linear * drag.Linear;
                }

                var effectiveMass = MathF.Max(0.0001f, mass.Value);
                acceleration.Value = force.Value / effectiveMass;
                entity.SetComponent(force);
                entity.SetComponent(acceleration);
            }
        }
    }
}
