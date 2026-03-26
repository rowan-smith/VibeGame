using System.Numerics;
using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Updates enemy intent toward the nearest player when inside aggro range.
    /// </summary>
    public class AISystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public AISystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            Vector3 playerPos = Vector3.Zero;
            bool hasPlayer = false;
            foreach (var player in _entities.GetEntitiesWith<PlayerComponent, CameraComponent>())
            {
                playerPos = player.GetComponent<CameraComponent>().Position;
                hasPlayer = true;
                break;
            }

            if (!hasPlayer)
                return;

            foreach (var entity in _entities.GetEntitiesWith<EnemyComponent, TransformComponent>())
            {
                var enemy = entity.GetComponent<EnemyComponent>();
                var transform = entity.GetComponent<TransformComponent>();
                var delta = playerPos - transform.Position;
                var distanceSq = delta.LengthSquared();
                var aggroSq = enemy.AggroRange * enemy.AggroRange;

                if (distanceSq <= aggroSq)
                {
                    enemy.State = 1;
                    if (entity.TryGetComponent<VelocityComponent>(out var velocity) && distanceSq > 1e-6f)
                    {
                        var dir = Vector3.Normalize(delta);
                        velocity.Linear = new Vector3(dir.X, velocity.Linear.Y, dir.Z) * 2.0f;
                        entity.SetComponent(velocity);
                    }
                }
                else
                {
                    enemy.State = 0;
                }

                entity.SetComponent(enemy);
            }
        }
    }
}

