namespace VibeGame.Terrain
{
    public class TerrainRingConfig
    {
        // Radius in heightmap chunks
        public int EditableRadius { get; set; } = 3;
        public int ReadOnlyRadius { get; set; } = 6;
        public int LowLodRadius { get; set; } = 12;

        // Safety caps
        public int MaxActiveVoxelChunks { get; set; } = 128;
    }
}
