using Raylib_CsLo;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active;
using Veilborne.GameWorlds.Active.Components;
using Veilborne.GameWorlds.Active.Entities;
using Veilborne.Interfaces;
using Veilborne.Utility;

namespace Veilborne.Systems.Core
{
    public class InputSystem : ISystem
    {
        public int Priority => 0;
        
        public SystemCategory Category => SystemCategory.Input;

        public bool RunsWhenPaused => true;

        private const float Speed = 5f;

        public void Initialize() {}

        public void Update(GameTime time, GameState state)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_P))
            {
                time.State = time.State == EngineState.Paused ? EngineState.Running : EngineState.Paused;
            }

            // Handle player movement only if running
            if (time.State != EngineState.Running)
            {
                return;
            }

            // Get local copy of position
            var player = state.Player;
            var t = player.Transform;
            var pos = t.Position;

            if (Raylib.IsKeyDown(KeyboardKey.KEY_W))
            {
                pos.Z += Speed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_S))
            {
                pos.Z -= Speed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_A))
            {
                pos.X += Speed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_D))
            {
                pos.X -= Speed * time.DeltaTime;
            }

            if (Raylib.IsKeyDown(KeyboardKey.KEY_SPACE))
            {
                var physics = player.GetComponent<PhysicsComponent>();
                if (physics != null && physics.IsGrounded)
                {
                    physics.Velocity.Y = 5f; // jump force
                    physics.IsGrounded = false;
                }
            }

            // Write back updated position
            t.Position = pos;
        }

        public void Shutdown() {}
    }
}
