namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores a human-readable name for debugging and gameplay metadata.
/// </summary>
 public struct NameComponent : IComponent
    {
        public NameComponent() { }

        public string Value { get; set; } = string.Empty;
    }
}


