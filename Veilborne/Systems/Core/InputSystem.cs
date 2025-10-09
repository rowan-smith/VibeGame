using System.Numerics;
using Raylib_CsLo;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active.Components;
using Veilborne.Interfaces;
using Veilborne.Utility;

namespace Veilborne.Systems.Core
{
    public class InputSystem : ISystem
    {
        public int Priority => 0;
        public SystemCategory Category => SystemCategory.Input;
        public bool RunsWhenPaused => true;

        private const float MoveSpeed = 5f;
        private const float JumpForce = 5f;

        public void Initialize() {}

        public void Update(GameTime time, GameState state)
        {
            // Toggle pause
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_P))
            {
                time.State = time.State == EngineState.Running ? EngineState.Paused : EngineState.Running;
            }

            if (time.State != EngineState.Running)
            {
                return;
            }

            var player = state.Player;
            var transform = player.Transform;
            var physics = player.GetComponent<PhysicsComponent>();

            // Get the forward/right vectors from the TransformComponent
            Vector3 forward = transform.Forward;
            forward.Y = 0;
            forward = Vector3.Normalize(forward);

            Vector3 right = transform.Right;
            right.Y = 0;
            right = Vector3.Normalize(right);

            Vector3 pos = transform.Position;

            Vector3 move = Vector3.Zero;

            if (Raylib.IsKeyDown(KeyboardKey.KEY_W))
            {
                move += forward;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_S))
            {
                move -= forward;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_A))
            {
                move += right;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_D))
            {
                move -= right;
            }

            // Apply movement
            if (move != Vector3.Zero)
            {
                pos += Vector3.Normalize(move) * MoveSpeed * time.DeltaTime;
            }

            // --- Jump ---
            if (Raylib.IsKeyDown(KeyboardKey.KEY_SPACE) && physics != null && physics.IsGrounded)
            {
                physics.Velocity.Y = JumpForce;
                physics.IsGrounded = false;
            }

            // Write back updated position
            transform.Position = pos;
        }

        public void Shutdown() {}
    }
}
