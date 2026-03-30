namespace Veilborne.Ecs.Components
{
/// <summary>
/// Stores terrain chunk coordinates associated with an entity.
/// </summary>
 public struct TerrainChunkComponent : IComponent
    {
        public TerrainChunkComponent() { }

        public int ChunkX { get; set; }

        public int ChunkZ { get; set; }

        public int LodLevel { get; set; }
    }
}


