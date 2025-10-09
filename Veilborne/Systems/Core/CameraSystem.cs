using System.Numerics;
using Raylib_CsLo;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active.Components;
using Veilborne.Interfaces;
using Veilborne.Utility;

namespace Veilborne.Systems.Core;

public class CameraSystem : ISystem
{
    private const float Sensitivity = 0.02f;   // radians per pixel
    private const float TargetHeight = 1.8f;   // player eye height

    public int Priority => 100;
    public SystemCategory Category => SystemCategory.Camera;
    public bool RunsWhenPaused => true;

    public void Initialize()
    {
        Raylib.DisableCursor();
    }

    public void Update(GameTime time, GameState state)
    {
        foreach (var entity in state.EntitiesWith<CameraComponent, TransformComponent>())
        {
            var cameraComp = entity.GetComponent<CameraComponent>();
            var transform = entity.GetComponent<TransformComponent>();

            // Mouse input
            var rotation = transform.Rotation;
            var delta = Raylib.GetMouseDelta();

            rotation.Y -= delta.X * Sensitivity;   // yaw
            rotation.X -= delta.Y * Sensitivity;   // pitch
            rotation.X = Math.Clamp(rotation.X, -MathF.PI / 2 + 0.001f, MathF.PI / 2 - 0.001f);

            transform.Rotation = rotation;

            // Update Camera3D
            var camera = cameraComp.Camera;
            camera.position = transform.Position + new Vector3(0, TargetHeight, 0);
            camera.target = camera.position + transform.Forward;
            camera.up = Vector3.UnitY;

            cameraComp.Camera = camera;
        }
    }

    public void Shutdown()
    {
        Raylib.EnableCursor();
    }
}
