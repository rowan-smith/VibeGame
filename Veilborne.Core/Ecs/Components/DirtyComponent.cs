namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Marks whether an entity requires a deferred update or rebuild.
/// </summary>
 public struct DirtyComponent : IComponent
    {
        public DirtyComponent() { }

        public bool NeedsUpdate { get; set; } = true;
    }
}


