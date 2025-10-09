using System.Numerics;

namespace Veilborne.GameWorlds.Active.Components;

public class TransformComponent : Component
{
    public Vector3 Position { get; set; } = Vector3.Zero;

    // Pitch = X (up/down), Yaw = Y (left/right)
    public Vector3 Rotation { get; set; } = Vector3.Zero;

    public Vector3 Scale { get; set; } = Vector3.One;

    public Vector3 Forward
    {
        get
        {
            float pitch = Rotation.X;
            float yaw = Rotation.Y;

            return Vector3.Normalize(new Vector3(
                MathF.Cos(pitch) * MathF.Sin(yaw),
                MathF.Sin(pitch),
                MathF.Cos(pitch) * MathF.Cos(yaw)
            ));
        }
    }

    public Vector3 Right
    {
        get
        {
            float yaw = Rotation.Y + MathF.PI / 2f;
            return Vector3.Normalize(new Vector3(MathF.Sin(yaw), 0, MathF.Cos(yaw)));
        }
    }

    public Vector3 Up => Vector3.Cross(Right, Forward);
}
