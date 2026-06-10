namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores the parent entity identifier for hierarchy relationships.
/// </summary>
 public struct ParentComponent : IComponent
    {
        public ParentComponent() { }

        public int EntityId { get; set; } = -1;
    }
}


