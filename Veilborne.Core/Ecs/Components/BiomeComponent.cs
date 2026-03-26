namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores biome identity metadata for terrain or world entities.
/// </summary>
 public struct BiomeComponent : IComponent
    {
        public BiomeComponent() { }

        public string BiomeId { get; set; } = string.Empty;
    }
}


