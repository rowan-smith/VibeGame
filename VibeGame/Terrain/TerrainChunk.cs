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

        // Dirty subregion tracking (inclusive bounds in local grid indices)
        public int DirtyMinX = int.MaxValue;
        public int DirtyMinZ = int.MaxValue;
        public int DirtyMaxX = int.MinValue;
        public int DirtyMaxZ = int.MinValue;

        public void MarkDirtyCell(int ix, int iz)
        {
            Dirty = true;
            if (ix < DirtyMinX) DirtyMinX = ix;
            if (iz < DirtyMinZ) DirtyMinZ = iz;
            if (ix > DirtyMaxX) DirtyMaxX = ix;
            if (iz > DirtyMaxZ) DirtyMaxZ = iz;
        }

        public void MarkDirtyRect(int x0, int z0, int x1, int z1)
        {
            Dirty = true;
            if (x0 < DirtyMinX) DirtyMinX = x0;
            if (z0 < DirtyMinZ) DirtyMinZ = z0;
            if (x1 > DirtyMaxX) DirtyMaxX = x1;
            if (z1 > DirtyMaxZ) DirtyMaxZ = z1;
        }

        public bool TryGetDirtyRect(out int x0, out int z0, out int x1, out int z1)
        {
            x0 = DirtyMinX; z0 = DirtyMinZ; x1 = DirtyMaxX; z1 = DirtyMaxZ;
            return x0 <= x1 && z0 <= z1 && x0 != int.MaxValue && z0 != int.MaxValue;
        }

        public void ClearDirty()
        {
            Dirty = false;
            DirtyMinX = int.MaxValue;
            DirtyMinZ = int.MaxValue;
            DirtyMaxX = int.MinValue;
            DirtyMaxZ = int.MinValue;
        }
    }
}
