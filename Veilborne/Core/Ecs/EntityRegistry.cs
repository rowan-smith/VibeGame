using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs
{
    public sealed class Entity
    {
        public Guid Id { get; } = Guid.NewGuid();
        private readonly Dictionary<Type, IComponent> _components = new();

        public T AddComponent<T>(T component) where T : IComponent
        {
            _components[typeof(T)] = component;
            return component;
        }

        public T GetComponent<T>() where T : IComponent
        {
            return (T)_components[typeof(T)];
        }

        public bool TryGetComponent<T>(out T component) where T : IComponent
        {
            if (_components.TryGetValue(typeof(T), out var comp))
            {
                component = (T)comp;
                return true;
            }
            component = default!;
            return false;
        }

        public bool HasComponent<T>() where T : IComponent
        {
            return _components.ContainsKey(typeof(T));
        }
    }

    public sealed class EntityRegistry
    {
        private readonly List<Entity> _entities = new();
        private readonly object _lock = new();

        public Entity CreateEntity()
        {
            var entity = new Entity();
            lock (_lock)
            {
                _entities.Add(entity);
            }
            return entity;
        }

        public void DestroyEntity(Entity entity)
        {
            lock (_lock)
            {
                _entities.Remove(entity);
            }
        }

        public IEnumerable<Entity> GetEntitiesWith<T>() where T : IComponent
        {
            var result = new List<Entity>();
            lock (_lock)
            {
                foreach (var entity in _entities)
                {
                    if (entity.HasComponent<T>())
                        result.Add(entity);
                }
            }
            return result;
        }

        public IEnumerable<Entity> GetEntitiesWith<T1, T2>() where T1 : IComponent where T2 : IComponent
        {
            var result = new List<Entity>();
            lock (_lock)
            {
                foreach (var entity in _entities)
                {
                    if (entity.HasComponent<T1>() && entity.HasComponent<T2>())
                        result.Add(entity);
                }
            }
            return result;
        }
    }
}
