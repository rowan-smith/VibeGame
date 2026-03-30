namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores mass properties and kinematic state for physics simulation.
/// </summary>
 public struct MassComponent : IComponent
    {
        public MassComponent() { }

        public float Value { get; set; } = 1f;

        public bool IsKinematic { get; set; } = false;
    }
}


