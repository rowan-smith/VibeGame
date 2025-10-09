using System.Numerics;
using Veilborne.Core.Enums;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.GameWorlds.Active.Components;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Systems.Core
{
    /// <summary>
    /// High-level input handler that drives player movement.
    /// This uses IInputProvider so it's engine-agnostic.
    /// </summary>
    public class InputSystem : IUpdateSystem
    {
        private readonly IInputProvider _input;

        public int Priority => 0;
        public SystemCategory Category => SystemCategory.Input;
        public bool RunsWhenPaused => true;

        private const float MoveSpeed = 5f;
        private const float JumpForce = 5f;

        public InputSystem(IInputProvider input)
        {
            _input = input;
        }

        public void Initialize() { }

        public void Update(GameTime time, GameState state)
        {
            // Toggle pause
            if (_input.IsKeyPressed(Key.P))
            {
                time.State = time.State == EngineState.Running
                    ? EngineState.Paused
                    : EngineState.Running;
            }

            if (time.State != EngineState.Running)
                return;

            var player = state.Player;
            var transform = player.Transform;
            var physics = player.GetComponent<PhysicsComponent>();

            // Flattened forward/right
            Vector3 forward = transform.Forward;
            forward.Y = 0;
            forward = Vector3.Normalize(forward);

            Vector3 right = transform.Right;
            right.Y = 0;
            right = Vector3.Normalize(right);

            Vector3 move = Vector3.Zero;

            if (_input.IsKeyDown(Key.W)) { move += forward; }
            if (_input.IsKeyDown(Key.S)) move -= forward;
            if (_input.IsKeyDown(Key.A)) move -= right;
            if (_input.IsKeyDown(Key.D)) move += right;

            if (move != Vector3.Zero)
                transform.Position += Vector3.Normalize(move) * MoveSpeed * time.DeltaTime;

            if (_input.IsKeyDown(Key.Space) && physics != null && physics.IsGrounded)
            {
                physics.Velocity.Y = JumpForce;
                physics.IsGrounded = false;
            }
        }

        public void Shutdown() { }
    }
}
