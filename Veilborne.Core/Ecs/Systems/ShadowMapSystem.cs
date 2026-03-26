using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Flags shadow casters as clean after post-physics updates.
    /// </summary>
    public class ShadowMapSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public ShadowMapSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<ShadowCasterComponent, DirtyComponent>())
            {
                var dirty = entity.GetComponent<DirtyComponent>();
                if (!dirty.NeedsUpdate)
                    continue;
                dirty.NeedsUpdate = false;
                entity.SetComponent(dirty);
            }
        }
    }
}
