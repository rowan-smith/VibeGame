using System.Numerics;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.GameWorlds.Active.Components;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Systems.Core
{
    /// <summary>
    /// Updates camera rotation and position based on mouse movement.
    /// Uses IMouseInput for abstraction (platform-independent).
    /// </summary>
    public class CameraSystem : IUpdateSystem
    {
        private readonly IMouseInput _mouse;
        private const float Sensitivity = 0.02f;
        private const float TargetHeight = 1.8f;

        public int Priority => 100;
        public SystemCategory Category => SystemCategory.Camera;
        public bool RunsWhenPaused => true;

        public CameraSystem(IMouseInput mouse)
        {
            _mouse = mouse;
        }

        public void Initialize()
        {
            _mouse.LockCursor(true);
        }

        public void Update(GameTime time, GameState state)
        {
            foreach (var entity in state.EntitiesWith<CameraComponent, TransformComponent>())
            {
                var cameraComp = entity.GetComponent<CameraComponent>();
                var transform = entity.GetComponent<TransformComponent>();

                // Mouse input → rotation
                Vector2 delta = _mouse.GetDelta();
                Vector3 rotation = transform.Rotation;

                rotation.Y -= delta.X * Sensitivity; // yaw
                rotation.X -= delta.Y * Sensitivity; // pitch
                rotation.X = Math.Clamp(rotation.X, -MathF.PI / 2 + 0.001f, MathF.PI / 2 - 0.001f);

                transform.Rotation = rotation;

                // Camera follows player
                cameraComp.Position = transform.Position + new Vector3(0, TargetHeight, 0);
                cameraComp.Target = cameraComp.Position + transform.Forward;
            }
        }

        public void Shutdown()
        {
            _mouse.LockCursor(false);
        }
    }
}
