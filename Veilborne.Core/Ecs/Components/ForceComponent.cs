using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores accumulated force to apply during physics integration.
/// </summary>
 public struct ForceComponent : IComponent
    {
        public ForceComponent() { }

        public Vector3 Value { get; set; } = Vector3.Zero;
    }
}


