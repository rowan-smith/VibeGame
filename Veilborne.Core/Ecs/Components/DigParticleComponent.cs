using System.Numerics;
using Veilborne.Terrain;

namespace Veilborne.Ecs.Components
{
    /// <summary>
    /// Marks an entity as a dig debris particle with velocity and lifetime.
    /// </summary>
    public struct DigParticleComponent : IComponent
    {
        public DigParticleComponent() { }

        public Vector3 Velocity { get; set; } = Vector3.Zero;
        public float Lifetime { get; set; } = 0.8f;
        public float Elapsed { get; set; } = 0f;
        public float Gravity { get; set; } = 9.8f;
        public ResourceBlockType BlockType { get; set; } = ResourceBlockType.None;
    }
}
