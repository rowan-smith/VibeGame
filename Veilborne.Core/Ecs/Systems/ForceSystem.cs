using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
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
            _entities.ForEachWith<CameraComponent>(entity =>
            {
                if (!entity.TryGetComponent<MassComponent>(out var mass) || mass.IsKinematic)
                    return;
                if (!entity.TryGetComponent<ForceComponent>(out var force))
                    return;
                if (!entity.TryGetComponent<AccelerationComponent>(out var acceleration))
                    return;

                if (entity.TryGetComponent<GravityComponent>(out var gravity))
                    force.Value += gravity.Direction * mass.Value;

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
            });
        }
    }
}
