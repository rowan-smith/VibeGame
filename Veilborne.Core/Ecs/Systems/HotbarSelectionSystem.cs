using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Settings;

namespace Veilborne.Core.Ecs.Systems
{
    /// <summary>
    /// Handles hotbar slot selection and applies current tool modifiers to dig interaction.
    /// </summary>
    public class HotbarSelectionSystem : ISystem
    {
        private readonly EntityRegistry _entities;
        private readonly IInputProvider _input;
        private readonly IGameSettingsService _settings;
        private readonly IItemRegistry _items;

        public HotbarSelectionSystem(EntityRegistry entities, IInputProvider input, IGameSettingsService settings, IItemRegistry items)
        {
            _entities = entities;
            _input = input;
            _settings = settings;
            _items = items;
        }

        public void Update(float dt)
        {
            var keyboard = _settings.Current.Keyboard;

            foreach (var entity in _entities.GetEntitiesWith<PlayerComponent, DigInteractionComponent>())
            {
                if (!entity.TryGetComponent<HotbarSelectionComponent>(out var hotbar))
                {
                    hotbar = new HotbarSelectionComponent { SelectedSlot = 0 };
                    entity.AddComponent(hotbar);
                }

                int slot = Math.Clamp(hotbar.SelectedSlot, 0, 8);

                float wheel = _input.GetMouseWheelMove();
                if (wheel != 0 && IsBindingConfigured(keyboard.Scroll))
                {
                    int delta = wheel > 0 ? -1 : 1;
                    slot = ((slot + delta) % 9 + 9) % 9;
                }
                else if (KeyBindingTokens.IsPressed(_input, keyboard.Scroll))
                {
                    slot = (slot + 1) % 9;
                }

                for (int i = 0; i < 9; i++)
                {
                    if (KeyBindingTokens.IsPressed(_input, GetHotbarBinding(keyboard, i)))
                        slot = i;
                }

                hotbar.SelectedSlot = slot;
                entity.SetComponent(hotbar);

                var dig = entity.GetComponent<DigInteractionComponent>();
                var item = _items.GetItemInSlot(slot);
                dig.ToolBreakSpeedMultiplier = Math.Clamp(item?.BreakSpeedMultiplier ?? 1f, 0.1f, 5f);
                dig.ToolStaminaCost = Math.Max(0, item?.StaminaCost ?? 0);
                entity.SetComponent(dig);
            }
        }

        private static InputBindingSettings GetHotbarBinding(KeyboardSettings keyboard, int index)
        {
            return index switch
            {
                0 => keyboard.Hotbar1,
                1 => keyboard.Hotbar2,
                2 => keyboard.Hotbar3,
                3 => keyboard.Hotbar4,
                4 => keyboard.Hotbar5,
                5 => keyboard.Hotbar6,
                6 => keyboard.Hotbar7,
                7 => keyboard.Hotbar8,
                8 => keyboard.Hotbar9,
                _ => keyboard.Hotbar1
            };
        }

        private static bool IsBindingConfigured(InputBindingSettings binding)
        {
            return KeyBindingTokens.Normalize(binding.Primary) != KeyBindingTokens.None ||
                   KeyBindingTokens.Normalize(binding.Secondary) != KeyBindingTokens.None;
        }
    }
}
