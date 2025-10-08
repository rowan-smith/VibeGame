using Raylib_CsLo;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active.Components;
using Veilborne.GameWorlds.Active.Entities;
using Veilborne.Interfaces;
using Veilborne.Utility;
using System.Numerics;

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

            if (time.State != EngineState.Running) return;

            var player = state.Player;
            var t = player.Transform;
            var pos = t.Position;

            // --- Camera-relative movement ---
            var cameraForward = Vector3.Normalize(new Vector3(
                _cameraSystem.Camera.target.X - _cameraSystem.Camera.position.X,
                0, // ignore vertical
                _cameraSystem.Camera.target.Z - _cameraSystem.Camera.position.Z
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
            if (Raylib.IsKeyDown(KeyboardKey.KEY_SPACE))
            {
                var physics = player.GetComponent<PhysicsComponent>();
                if (physics != null && physics.IsGrounded)
                {
                    physics.Velocity.Y = JumpForce;
                    physics.IsGrounded = false;
                }
            }

            // Write back updated position
            t.Position = pos;
        }

        public void Shutdown() {}
    }
}
