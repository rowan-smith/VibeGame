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

        private CameraSystem _cameraSystem;

        public InputSystem(CameraSystem cameraSystem)
        {
            _cameraSystem = cameraSystem;
        }

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

            if (Raylib.IsKeyDown(KeyboardKey.KEY_W))
            {
                pos += forward * MoveSpeed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_S))
            {
                pos -= forward * MoveSpeed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_A))
            {
                pos -= right * MoveSpeed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_D))
            {
                pos += right * MoveSpeed * time.DeltaTime;
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
