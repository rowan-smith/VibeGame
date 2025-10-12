namespace VibeGame.Terrain
{
    public class TerrainRingConfig
    {
        // Base radii (starting point)
        public int EditableRadius { get; set; } = 1;
        public int ReadOnlyRadius { get; set; } = 3;
        public int LowLodRadius { get; set; } = 8;

        // Min/Max caps for adaptive sizing
        public int MinEditable { get; set; } = 1;
        public int MaxEditable { get; set; } = 2;
        public int MinReadOnly { get; set; } = 2;
        public int MaxReadOnly { get; set; } = 8;
        public int MinLowLod { get; set; } = 6;
        public int MaxLowLod { get; set; } = 24;

        // Update pacing (in frames)
        // ReadOnly ring updates every N frames; LowLod updates every N*2 frames.
        public int ReadOnlyUpdateInterval { get; set; } = 2;

        // Tuning multipliers
        public float SpeedScale { get; set; } = 0.15f;    // chunks per m/s
        public float DensityPenalty { get; set; } = 1.0f; // reduce chunks under high roughness
        public float FpsTarget { get; set; } = 60f;       // target framerate for budget heuristic

        // Mesh build throttling
        public int MaxMeshBuildsPerFrame { get; set; } = 3;
    }
}
