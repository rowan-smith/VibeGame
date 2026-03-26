using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Applies lightweight constraints to keep transient state coherent.
    /// </summary>
    public class ConstraintSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public ConstraintSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<VelocityComponent>())
            {
                var velocity = entity.GetComponent<VelocityComponent>();
                if (!float.IsFinite(velocity.Linear.X) ||
                    !float.IsFinite(velocity.Linear.Y) ||
                    !float.IsFinite(velocity.Linear.Z))
                {
                    velocity.Linear = System.Numerics.Vector3.Zero;
                    entity.SetComponent(velocity);
                }
            }
        }
    }
}
