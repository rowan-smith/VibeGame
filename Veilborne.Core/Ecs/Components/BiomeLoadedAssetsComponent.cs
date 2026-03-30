namespace Veilborne.Core.Ecs.Components
{
/// <summary>
/// Stores loaded asset references for a biome bundle.
/// </summary>
 public struct BiomeLoadedAssetsComponent : IComponent
    {
        public BiomeLoadedAssetsComponent() { }

        public string BiomeId { get; set; } = string.Empty;

        public string GrassTexturePath { get; set; } = string.Empty;

        public string TreeModelPath { get; set; } = string.Empty;

        public int ActiveChunkRefs { get; set; } = 0;
    }
}

