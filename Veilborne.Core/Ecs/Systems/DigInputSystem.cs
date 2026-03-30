using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Settings;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Samples dig binding state into ECS interaction intent.
    /// </summary>
    public class DigInputSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInputProvider _input;
        private readonly IGameSettingsService _settings;

        public DigInputSystem(EntityRegistry entities, IInputProvider input, IGameSettingsService settings)
        {
            _entities = entities;
            _input = input;
            _settings = settings;
        }

        public void Update(float dt)
        {
            var binding = _settings.Current.Keyboard.DigInteract;
            var isHeld = KeyBindingTokens.IsDown(_input, binding);

            _entities.ForEachWith<DigInteractionComponent>(entity =>
            {
                var dig = entity.GetComponent<DigInteractionComponent>();
                dig.IsDigHeld = isHeld;
                entity.SetComponent(dig);
            });
        }
    }
}

