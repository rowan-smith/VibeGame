using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores linear velocity in world units per second.
/// </summary>
 public struct VelocityComponent : IComponent
    {
        public VelocityComponent() { }

        public Vector3 Linear { get; set; } = Vector3.Zero;
    }
}


