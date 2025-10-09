using System.Numerics;
using Raylib_CsLo;

namespace Veilborne.GameWorlds.Active.Components;

public class CameraComponent : Component
{
    public Camera3D Camera { get; set; } = new Camera3D
    {
        position = new Vector3(0, 1.8f, -5),
        target = Vector3.Zero,
        up = Vector3.UnitY,
        fovy = 45,
        projection_ = CameraProjection.CAMERA_PERSPECTIVE
    };
}
