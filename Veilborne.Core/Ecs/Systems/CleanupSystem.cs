using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Removes entities whose finite lifetime has expired.
    /// </summary>
    public class CleanupSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly List<Entity> _toDestroy = new();

        public CleanupSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            _toDestroy.Clear();
            _entities.ForEachWith<LifetimeComponent>((Entity entity, ref LifetimeComponent lifetime) =>
            {
                if (lifetime.RemainingSeconds <= 0f)
                    return;

                lifetime.RemainingSeconds -= dt;
                if (lifetime.RemainingSeconds <= 0f)
                    _toDestroy.Add(entity);
            });

            foreach (var entity in _toDestroy)
                _entities.DestroyEntity(entity);
        }
    }
}
