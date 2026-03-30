using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Maintains lightweight visual-effect state derived from lifetime.
    /// </summary>
    public class EffectSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public EffectSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<LifetimeComponent, RenderComponent>())
            {
                var lifetime = entity.GetComponent<LifetimeComponent>();
                if (lifetime.RemainingSeconds > 0f && lifetime.RemainingSeconds < 0.15f)
                {
                    var render = entity.GetComponent<RenderComponent>();
                    if (render.Visible)
                    {
                        render.Visible = false;
                        entity.SetComponent(render);
                    }
                }
            }
        }
    }
}
