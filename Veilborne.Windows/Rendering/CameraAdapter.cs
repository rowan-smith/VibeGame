using Microsoft.Xna.Framework;
using Veilborne.Core.GameWorlds.Active.Components;
using Veilborne.Core.Interfaces;
using Veilborne.Windows.Utility;
using Vector3 = System.Numerics.Vector3;

namespace Veilborne.Windows.Rendering;

public class CameraAdapter : ICamera
{
    private readonly CameraComponent _component;

    public CameraAdapter(CameraComponent component)
    {
        _component = component;
    }

    public float FieldOfView => _component.FieldOfView;

    public float NearPlane => _component.NearPlane;

    public float FarPlane => _component.FarPlane;

    // Required by ICamera
    public Vector3 Position => _component.Position;

    public Vector3 Target => _component.Target;

    public Vector3 Up => _component.Up;

    // Extra convenience properties for MonoGame renderer
    public Matrix View => Matrix.CreateLookAt(_component.Position.ToXna(), _component.Target.ToXna(), _component.Up.ToXna());

    public Matrix Projection =>
        _component.IsPerspective
            ? Matrix.CreatePerspectiveFieldOfView(
                MathHelper.ToRadians(_component.FieldOfView),
                16f / 9f, // TODO: replace with dynamic aspect ratio later
                _component.NearPlane,
                _component.FarPlane)
            : Matrix.CreateOrthographic(16, 9, _component.NearPlane, _component.FarPlane);
}
