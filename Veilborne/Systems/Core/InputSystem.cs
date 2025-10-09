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
            var pos = transform.Position;

            // Get camera for this player
            var cameraComp = player.GetComponent<CameraComponent>();
            if (cameraComp == null)
            {
                return;
            }

            var cameraForward = Vector3.Normalize(new Vector3(
                cameraComp.Camera.target.X - cameraComp.Camera.position.X,
                0, // ignore vertical for horizontal movement
                cameraComp.Camera.target.Z - cameraComp.Camera.position.Z
            ));
            var cameraRight = Vector3.Normalize(Vector3.Cross(cameraForward, Vector3.UnitY));

            if (Raylib.IsKeyDown(KeyboardKey.KEY_W))
            {
                pos += cameraForward * MoveSpeed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_S))
            {
                pos -= cameraForward * MoveSpeed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_A))
            {
                pos -= cameraRight * MoveSpeed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_D))
            {
                pos += cameraRight * MoveSpeed * time.DeltaTime;
            }

            // --- Jump ---
            var physics = player.GetComponent<PhysicsComponent>();
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
