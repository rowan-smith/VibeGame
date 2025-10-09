using System.Numerics;

namespace Veilborne.Core.GameWorlds.Active.Components;

public class CameraComponent : Component
{
    /// <summary>World position of the camera.</summary>
    public Vector3 Position { get; set; } = new(0, 1.8f, -5);

    /// <summary>Point the camera is looking at.</summary>
    public Vector3 Target { get; set; } = Vector3.Zero;

    /// <summary>Up direction of the camera (usually Y axis).</summary>
    public Vector3 Up { get; set; } = Vector3.UnitY;

    /// <summary>Field of view in degrees.</summary>
    public float FieldOfView { get; set; } = 45f;

    /// <summary>Whether the camera uses perspective projection (vs orthographic).</summary>
    public bool IsPerspective { get; set; } = true;

    /// <summary>Optional near and far clip distances.</summary>
    public float NearPlane { get; set; } = 0.1f;

    public float FarPlane { get; set; } = 1000f;
}
