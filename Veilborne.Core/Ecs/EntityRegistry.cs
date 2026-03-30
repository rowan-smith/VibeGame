using EcsEntityStore = Friflo.Engine.ECS.EntityStore;
using EcsComponent = Veilborne.Ecs.Components.IComponent;

namespace Veilborne.Ecs
{
public readonly struct Entity
{
    private readonly Friflo.Engine.ECS.Entity _inner;

    internal Entity(Friflo.Engine.ECS.Entity inner)
    {
        _inner = inner;
    }

    public int Id => _inner.Id;

    public T AddComponent<T>(T component) where T : struct, EcsComponent
    {
        _inner.AddComponent(component);
        return component;
    }

    public T GetComponent<T>() where T : struct, EcsComponent
    {
        return _inner.GetComponent<T>();
    }

    public bool TryGetComponent<T>(out T component) where T : struct, EcsComponent
    {
        return _inner.TryGetComponent(out component);
    }

    public bool HasComponent<T>() where T : struct, EcsComponent
    {
        return _inner.HasComponent<T>();
    }

    public void SetComponent<T>(T component) where T : struct, EcsComponent
    {
        _inner.GetComponent<T>() = component;
    }
}

    public sealed class EntityRegistry
    {
        private readonly EcsEntityStore _store = new();

    public Entity CreateEntity()
    {
        var entity = _store.CreateEntity();
        return new Entity(entity);
    }

        public void DestroyEntity(Entity entity)
        {
            _store.GetEntityById(entity.Id).DeleteEntity();
        }

        public IEnumerable<Entity> GetEntitiesWith<T>() where T : struct, EcsComponent
        {
            var query = _store.Query<T>();
        var entities = new List<Entity>();
        query.ForEachEntity((ref T _, Friflo.Engine.ECS.Entity ecsEntity) =>
        {
            entities.Add(new Entity(ecsEntity));
        });
        return entities;
    }

        public void ForEachWith<T>(Action<Entity> callback) where T : struct, EcsComponent
        {
            var query = _store.Query<T>();
            query.ForEachEntity((ref T _, Friflo.Engine.ECS.Entity ecsEntity) =>
            {
                callback(new Entity(ecsEntity));
            });
        }

        public IEnumerable<Entity> GetEntitiesWith<T1, T2>() where T1 : struct, EcsComponent where T2 : struct, EcsComponent
        {
            var query = _store.Query<T1, T2>();
        var entities = new List<Entity>();
        query.ForEachEntity((ref T1 _, ref T2 __, Friflo.Engine.ECS.Entity ecsEntity) =>
        {
            entities.Add(new Entity(ecsEntity));
        });
        return entities;
    }

        public void ForEachWith<T1, T2>(Action<Entity> callback) where T1 : struct, EcsComponent where T2 : struct, EcsComponent
        {
            var query = _store.Query<T1, T2>();
            query.ForEachEntity((ref T1 _, ref T2 __, Friflo.Engine.ECS.Entity ecsEntity) =>
            {
                callback(new Entity(ecsEntity));
            });
        }
}
}
