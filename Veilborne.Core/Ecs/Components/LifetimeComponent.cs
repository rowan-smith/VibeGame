namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores remaining lifetime in seconds for temporary entities.
/// </summary>
 public struct LifetimeComponent : IComponent
    {
        public LifetimeComponent() { }

        public float RemainingSeconds { get; set; } = 0f;
    }
}


