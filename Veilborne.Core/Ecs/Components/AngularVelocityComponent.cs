using System.Numerics;

namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores angular velocity in radians per second.
/// </summary>
 public struct AngularVelocityComponent : IComponent
    {
        public AngularVelocityComponent() { }

        public Vector3 Angular { get; set; } = Vector3.Zero;
    }
}

