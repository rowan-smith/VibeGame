using System.Numerics;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Settings;

namespace Veilborne.Ecs.Systems
{
    /// <summary>
    /// Maps player control bindings to ECS movement/jump intent components.
    /// </summary>
    public class PlayerInputSystem : ISystem
    {
        private const float BaseMoveSpeed = 7.5f;
        private readonly EntityRegistry _entities;
        private readonly IInputProvider _input;
        private readonly IGameSettingsService _settings;

        public PlayerInputSystem(
            EntityRegistry entities,
            IInputProvider input,
            IGameSettingsService settings)
        {
            _entities = entities;
            _input = input;
            _settings = settings;
        }

        public void Update(float dt)
        {
            var keyboard = _settings.Current.Keyboard;
            var debug = _settings.Current.Debug;
            float speed = _input.IsKeyDown(InputKeys.KEY_LEFT_SHIFT) ? BaseMoveSpeed * 1.75f : BaseMoveSpeed;
            if (RuntimeEnvironment.IsDevelopmentEnvironment)
                speed *= debug.RunSpeedMultiplier / 100f;

            _entities.ForEachWith<PlayerComponent, CameraComponent>((Entity entity, ref PlayerComponent _, ref CameraComponent cam) =>
            {
                if (!entity.TryGetComponent<MoveInputComponent>(out var moveInput))
                    return;

                var forward = Vector3.Normalize(cam.Target - cam.Position);
                var flatForward = new Vector3(forward.X, 0f, forward.Z);
                if (flatForward.LengthSquared() < 1e-6f)
                    flatForward = Vector3.UnitZ;
                else
                    flatForward = Vector3.Normalize(flatForward);
                var flatRight = Vector3.Normalize(Vector3.Cross(flatForward, cam.Up));

                Vector3 moveDir = Vector3.Zero;
                if (KeyBindingTokens.IsDown(_input, keyboard.Forward)) moveDir += flatForward;
                if (KeyBindingTokens.IsDown(_input, keyboard.Backward)) moveDir -= flatForward;
                if (KeyBindingTokens.IsDown(_input, keyboard.Left)) moveDir -= flatRight;
                if (KeyBindingTokens.IsDown(_input, keyboard.Right)) moveDir += flatRight;
                if (moveDir.LengthSquared() > 1e-6f)
                    moveDir = Vector3.Normalize(moveDir);

                moveInput.HorizontalDisplacement = moveDir * (speed * dt);
                entity.SetComponent(moveInput);

                if (entity.TryGetComponent<JumpComponent>(out var jump) &&
                    KeyBindingTokens.IsPressed(_input, keyboard.Jump))
                {
                    jump.JumpBufferTimer = jump.JumpBufferSeconds;
                    entity.SetComponent(jump);
                }
            });
        }
    }
}
