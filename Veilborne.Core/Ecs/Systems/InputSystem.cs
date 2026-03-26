using Veilborne.Core.Ecs.Components;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Clears per-frame input intents before the player input system repopulates them.
    /// </summary>
    public class InputSystem : ISystem
    {
        private readonly EntityRegistry _entities;

        public InputSystem(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Update(float dt)
        {
            foreach (var entity in _entities.GetEntitiesWith<MoveInputComponent>())
            {
                var input = entity.GetComponent<MoveInputComponent>();
                if (input.HorizontalDisplacement != System.Numerics.Vector3.Zero)
                {
                    input.HorizontalDisplacement = System.Numerics.Vector3.Zero;
                    entity.SetComponent(input);
                }
            }
        }
    }
}
