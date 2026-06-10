using Veilborne.Ecs.Components;

namespace Veilborne.Ecs.Systems
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
            _entities.ForEachWith<MoveInputComponent>((Entity entity, ref MoveInputComponent input) =>
            {
                if (input.HorizontalDisplacement != System.Numerics.Vector3.Zero)
                    input.HorizontalDisplacement = System.Numerics.Vector3.Zero;
            });
        }
    }
}
