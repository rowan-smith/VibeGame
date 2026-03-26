using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Finalizes generated vegetation entities by clearing dirty flags after install.
    /// </summary>
    public class VegetationSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public VegetationSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<WorldObjectComponent, DirtyComponent>())
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
