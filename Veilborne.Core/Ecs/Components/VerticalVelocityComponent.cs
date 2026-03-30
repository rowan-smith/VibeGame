namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores vertical velocity used by jump and gravity integration.
/// </summary>
 public struct VerticalVelocityComponent : IComponent
    {
        public VerticalVelocityComponent() { }

        public float Value { get; set; }
    }
}


