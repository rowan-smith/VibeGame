namespace Veilborne.Core.Ecs.Components
{
    public struct TerrainPatchDirtyComponent : IComponent
    {
        public TerrainPatchDirtyComponent() { }

        public int ChunkX { get; set; }
        public int ChunkZ { get; set; }
        public int MinX { get; set; }
        public int MinZ { get; set; }
        public int MaxX { get; set; }
        public int MaxZ { get; set; }
    }
}
