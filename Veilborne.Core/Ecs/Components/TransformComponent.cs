using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores world-space position, rotation, and scale.
/// </summary>
 public struct TransformComponent : IComponent
    {
        public TransformComponent() { }

        public Vector3 Position { get; set; } = Vector3.Zero;

        public Quaternion Rotation { get; set; } = Quaternion.Identity;

        public Vector3 Scale { get; set; } = Vector3.One;
    }
}


