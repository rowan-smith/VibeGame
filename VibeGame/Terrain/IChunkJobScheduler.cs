using VibeGame.Objects;

namespace VibeGame.Terrain
{
    public enum ChunkJobType { Load, Unload }

    public readonly struct HeightmapChunkResult
    {
        public readonly (int cx, int cz) Key;
        public readonly float[,] Heights;
        public readonly List<SpawnedObject> Objects;
        public readonly ChunkState TargetState;

        public HeightmapChunkResult((int cx, int cz) key, float[,] heights, List<SpawnedObject> objects, ChunkState state)
        {
            Key = key;
            Heights = heights;
            Objects = objects;
            TargetState = state;
        }
    }

    public interface IChunkJobScheduler
    {
        void EnqueueLoad((int cx, int cz) index, ChunkState targetState);
        void EnqueueUnload((int cx, int cz) index);
        bool TryDequeueApply(out HeightmapChunkResult result);
        void Stop();
    }
}
