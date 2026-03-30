namespace Veilborne.Ecs.Components
{
/// <summary>
/// Holds biome asset bundle metadata and load state.
/// </summary>
 public struct AssetBundleComponent : IComponent
    {
        public AssetBundleComponent() { }

        public string BiomeId { get; set; } = string.Empty;

        public bool IsLoaded { get; set; } = false;
    }
}

