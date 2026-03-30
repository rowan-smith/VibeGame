using Veilborne.Core.Terrain;

namespace Veilborne.Core.Interfaces
{
    public interface IChunkJobScheduler
    {
        void EnqueueLoad((int cx, int cz) index, ChunkState targetState);

        void EnqueueUnload((int cx, int cz) index);

        bool TryDequeueApply(out HeightmapChunkResult result);

        void Stop();
    }
}
