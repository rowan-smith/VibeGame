namespace Veilborne.Ecs.Components
{
/// <summary>
/// Requests loading of biome-specific asset bundles with priority.
/// </summary>
 public struct BiomeLoadRequestComponent : IComponent
    {
        public BiomeLoadRequestComponent() { }

        public string BiomeId { get; set; } = string.Empty;

        public int Priority { get; set; } = 0;
    }
}

