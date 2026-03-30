using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Removes entities whose finite lifetime has expired.
    /// </summary>
    public class CleanupSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public CleanupSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            var toDestroy = new List<Entity>();
            foreach (var entity in _entities.GetEntitiesWith<LifetimeComponent>())
            {
                var lifetime = entity.GetComponent<LifetimeComponent>();
                if (lifetime.RemainingSeconds <= 0f)
                    continue;

                lifetime.RemainingSeconds -= dt;
                if (lifetime.RemainingSeconds <= 0f)
                {
                    toDestroy.Add(entity);
                }
                else
                {
                    entity.SetComponent(lifetime);
                }
            }

            foreach (var entity in toDestroy)
            {
                _entities.DestroyEntity(entity);
            }
        }
    }
}
