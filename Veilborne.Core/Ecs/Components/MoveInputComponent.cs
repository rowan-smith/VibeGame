using System.Numerics;

namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores frame movement intent produced by input systems.
/// </summary>
 public struct MoveInputComponent : IComponent
    {
        public MoveInputComponent() { }

        public Vector3 HorizontalDisplacement { get; set; } = Vector3.Zero;
    }
}


