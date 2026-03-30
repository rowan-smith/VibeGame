namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores child entity identifiers for hierarchy relationships.
/// </summary>
 public struct ChildrenComponent : IComponent
    {
        public ChildrenComponent() { }

        public int[] EntityIds { get; set; } = [];
    }
}


