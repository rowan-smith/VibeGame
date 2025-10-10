using System.Numerics;
using Veilborne.Core.GameWorlds.Terrain;

namespace Veilborne.Core.Interfaces
{
    public interface IHeightmapGenerator
    {
        float ComputeHeight(float worldX, float worldZ);

        // Called by ReadOnlyTerrainService to fill chunk data
        void SampleRegion(Vector3 origin, TerrainChunk chunk);
    }
}
