using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Maintains debug tags for selected entity categories.
    /// </summary>
    public class DebugDrawSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public DebugDrawSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<PlayerComponent, TagComponent>())
            {
                var tag = entity.GetComponent<TagComponent>();
                if (tag.Name != "Player")
                {
                    tag.Name = "Player";
                    entity.SetComponent(tag);
                }
            }
        }
    }
}
