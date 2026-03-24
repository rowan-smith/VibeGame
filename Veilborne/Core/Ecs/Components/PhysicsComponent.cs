using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
    public class PhysicsComponent : IComponent
    {
        public float CollisionRadius { get; set; } = 0f;

        public bool IsStatic { get; set; } = true;

        public Vector3 Velocity { get; set; } = Vector3.Zero;
    }
}
