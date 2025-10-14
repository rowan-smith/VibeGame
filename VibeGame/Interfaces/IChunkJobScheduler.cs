namespace VibeGame.Terrain
{
    public interface IChunkJobScheduler
    {
        void EnqueueLoad((int cx, int cz) index, ChunkState targetState);

        void EnqueueUnload((int cx, int cz) index);

        bool TryDequeueApply(out HeightmapChunkResult result);

        void Stop();
    }
}
