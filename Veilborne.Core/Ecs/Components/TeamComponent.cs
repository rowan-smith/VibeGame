namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores team identifier used by game logic and faction checks.
/// </summary>
 public struct TeamComponent : IComponent
    {
        public TeamComponent() { }

        public int Id { get; set; } = 0;
    }
}


