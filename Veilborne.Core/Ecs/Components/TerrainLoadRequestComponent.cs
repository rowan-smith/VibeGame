namespace Veilborne.Ecs.Components
{
/// <summary>
/// Represents a pending terrain chunk load request with queue priority.
/// </summary>
 public struct TerrainLoadRequestComponent : IComponent
    {
        public TerrainLoadRequestComponent() { }

        public int ChunkX { get; set; }

        public int ChunkZ { get; set; }

        public int Priority { get; set; }
    }
}

