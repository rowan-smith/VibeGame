using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Settings;
using Veilborne.Interfaces;

namespace Veilborne.Core.Ecs.Systems
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

            foreach (var entity in _entities.GetEntitiesWith<DigInteractionComponent>())
            {
                var dig = entity.GetComponent<DigInteractionComponent>();
                dig.IsDigHeld = isHeld;
                entity.SetComponent(dig);
            }
        }
    }
}

