using System.Numerics;
using ZeroElectric.Vinculum;

namespace Veilborne.Core.GameWorlds.Terrain
{
    public class TerrainChunk
    {
        public float[,] Heights;
        public Vector2 Origin;
        public bool IsMeshGenerated = false;

        // Change tracking for caching and invalidation
        public bool Dirty = false;
        public int Version = 0;

        // Optional: version of the source data this mesh was last built from
        public int BuiltFromVersion = -1;
    }
}
