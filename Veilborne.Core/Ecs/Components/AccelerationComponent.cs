using System.Numerics;

namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores linear acceleration for motion integration.
/// </summary>
 public struct AccelerationComponent : IComponent
    {
        public AccelerationComponent() { }

        public Vector3 Value { get; set; } = Vector3.Zero;
    }
}


