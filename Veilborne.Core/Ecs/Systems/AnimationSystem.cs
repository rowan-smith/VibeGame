using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Advances animation timelines and picks clips from movement state.
    /// </summary>
    public class AnimationSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public AnimationSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<AnimationComponent>())
            {
                var animation = entity.GetComponent<AnimationComponent>();
                animation.TimeSeconds += dt * animation.Speed;

                if (entity.TryGetComponent<VelocityComponent>(out var velocity))
                {
                    animation.Clip = velocity.Linear.LengthSquared() > 0.05f
                        ? "Run"
                        : "Idle";
                }

                entity.SetComponent(animation);
            }
        }
    }
}

