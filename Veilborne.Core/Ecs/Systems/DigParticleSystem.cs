using System.Numerics;
using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Updates dig debris particles: applies velocity, gravity, and removes expired ones.
    /// </summary>
    public class DigParticleSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly List<Entity> _expiredBuffer = new();

        public DigParticleSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            _expiredBuffer.Clear();
            _entities.ForEachWith<DigParticleComponent, TransformComponent>(entity =>
            {
                var particle = entity.GetComponent<DigParticleComponent>();
                particle.Elapsed += dt;

                if (particle.Elapsed >= particle.Lifetime)
                {
                    _expiredBuffer.Add(entity);
                    return;
                }

                var transform = entity.GetComponent<TransformComponent>();
                particle.Velocity = new Vector3(
                    particle.Velocity.X * 0.98f,
                    particle.Velocity.Y - particle.Gravity * dt,
                    particle.Velocity.Z * 0.98f);
                transform.Position += particle.Velocity * dt;

                // Shrink over lifetime
                float life = 1f - particle.Elapsed / particle.Lifetime;
                float scale = MathF.Max(0.01f, life * 0.15f);
                transform.Scale = new Vector3(scale, scale, scale);

                entity.SetComponent(particle);
                entity.SetComponent(transform);
            });

            foreach (var entity in _expiredBuffer)
                _entities.DestroyEntity(entity);
        }
    }
}
