using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Updates simple particle emitter spawn/live counts each frame.
    /// </summary>
    public class ParticleSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public ParticleSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<ParticleEmitterComponent>())
            {
                var emitter = entity.GetComponent<ParticleEmitterComponent>();
                emitter.SpawnAccumulator += emitter.SpawnRate * dt;

                if (emitter.SpawnAccumulator >= 1f)
                {
                    var spawnCount = (int)emitter.SpawnAccumulator;
                    emitter.LiveCount = System.Math.Min(emitter.MaxCount, emitter.LiveCount + spawnCount);
                    emitter.SpawnAccumulator -= spawnCount;
                }
                else if (emitter.LiveCount > 0)
                {
                    emitter.LiveCount = System.Math.Max(0, emitter.LiveCount - 1);
                }

                entity.SetComponent(emitter);
            }
        }
    }
}

