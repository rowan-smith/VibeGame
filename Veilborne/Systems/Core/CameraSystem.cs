using System.Numerics;
using Raylib_CsLo;
using Veilborne.GameWorlds;
using Veilborne.Interfaces;
using Veilborne.Utility;

namespace Veilborne.Systems.Core;

public class CameraSystem : ISystem
{
    private Camera3D _camera;

    // Mouse-look state
    private float _yaw;    // radians, around Y axis
    private float _pitch;  // radians, up/down
    private bool _mouseCaptured = true;

    // Tunables
    private const float Sensitivity = 0.02f;   // radians per pixel
    private const float FollowLerp = 10f;        // how quickly camera target follows player
    private const float Distance = 10f;          // orbit radius
    private const float TargetHeight = 1.0f;     // player eye height

    public int Priority => 100;
    public SystemCategory Category => SystemCategory.Camera;
    public bool RunsWhenPaused => true; // camera moves even when paused

    public CameraSystem()
    {
        _camera = new Camera3D
        {
            position = new Vector3(0, 10, 10),
            target = Vector3.Zero,
            up = Vector3.UnitY,
            fovy = 45,
            projection_ = CameraProjection.CAMERA_PERSPECTIVE,
        };

        _yaw = 0f;
        _pitch = -0.35f; // slightly pitched down
    }

    public void Initialize()
    {
        Raylib.DisableCursor(); // hide and lock cursor
        _mouseCaptured = true;
    }

    public void Update(GameTime time, GameState state)
    {
        var playerPos = state.Player.Transform.Position + new Vector3(0, TargetHeight, 0);

        if (_mouseCaptured)
        {
            var delta = Raylib.GetMouseDelta();
            _yaw += delta.X * Sensitivity;
            _pitch -= delta.Y * Sensitivity;
            _pitch = Math.Clamp(_pitch, -1.553343f, 1.553343f); // ±89°
        }

        var forward = new Vector3(
            MathF.Cos(_pitch) * MathF.Sin(_yaw),
            MathF.Sin(_pitch),
            MathF.Cos(_pitch) * MathF.Cos(_yaw)
        );

        _camera.position = playerPos;
        _camera.target = playerPos + forward;
        _camera.up = Vector3.UnitY;

        Raylib.UpdateCamera(ref _camera);
    }

    public void Shutdown()
    {
        Raylib.EnableCursor(); // restore cursor on shutdown
    }

    public Camera3D Camera => _camera;
}
