namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores linear and angular drag coefficients for motion damping.
/// </summary>
 public struct DragComponent : IComponent
    {
        public DragComponent() { }

        public float Linear { get; set; } = 0f;

        public float Angular { get; set; } = 0f;
    }
}


