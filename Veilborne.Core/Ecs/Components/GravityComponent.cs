using System.Numerics;

namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores gravity direction applied to an entity.
/// </summary>
 public struct GravityComponent : IComponent
    {
        public GravityComponent() { }

        public Vector3 Direction { get; set; } = new(0f, -20f, 0f);
    }
}


