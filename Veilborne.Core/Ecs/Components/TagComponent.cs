namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores a lightweight categorical tag for gameplay queries.
/// </summary>
 public struct TagComponent : IComponent
    {
        public TagComponent() { }

        public string Name { get; set; } = string.Empty;
    }
}


