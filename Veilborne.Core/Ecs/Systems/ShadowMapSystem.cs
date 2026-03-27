using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Sky;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Flags shadow casters as clean after post-physics updates.
    /// </summary>
    public class ShadowMapSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IShadowMapService _shadowMap;

        public ShadowMapSystem(EntityRegistry entities, IShadowMapService shadowMap)
        {
            _entities = entities;
            _shadowMap = shadowMap;
        }

        public void Update(float dt)
        {
            _shadowMap.Update(dt);
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
