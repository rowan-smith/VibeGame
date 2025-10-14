namespace VibeGame.Core
{
    public sealed class WorldConfig
    {
        public int WorldSeed { get; set; } = 0;
        public int EditableRadius { get; set; } = 3;
        public int ReadOnlyRadius { get; set; } = 6;
        public int LowLodRadius { get; set; } = 12;
        public int MaxActiveVoxelChunks { get; set; } = 128;
    }
}
