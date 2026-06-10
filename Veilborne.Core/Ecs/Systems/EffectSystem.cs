using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
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
            _entities.ForEachWith<LifetimeComponent, RenderComponent>((Entity entity, ref LifetimeComponent lifetime, ref RenderComponent render) =>
            {
                if (lifetime.RemainingSeconds > 0f && lifetime.RemainingSeconds < 0.15f)
                    render.Visible = false;
            });
        }
    }
}
