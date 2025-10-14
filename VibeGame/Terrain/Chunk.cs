using System.Numerics;

namespace VibeGame.Terrain
{
    // Generic chunk used by World.ActiveChunks for bookkeeping across rings.
    // This is intentionally lightweight; existing services keep their own chunk types internally.
    public sealed class Chunk
    {
        public Vector3 Position { get; set; } // chunk origin in world space
        public int Lod { get; set; }
        public object? Mesh { get; set; } // placeholder for mesh/GPU handle
        public bool Dirty { get; private set; }

        public Chunk(Vector3 position, int lod)
        {
            Position = position;
            Lod = lod;
            Dirty = false;
        }

        public void MarkDirty() => Dirty = true;
        public void ClearDirty() => Dirty = false;

        public byte[] Serialize()
        {
            // Minimal placeholder; real implementation would write mesh/voxel deltas
            var bytes = new List<byte>(16);
            void WriteInt(int v) { bytes.AddRange(BitConverter.GetBytes(v)); }
            void WriteFloat(float f) { bytes.AddRange(BitConverter.GetBytes(f)); }
            WriteFloat(Position.X);
            WriteFloat(Position.Y);
            WriteFloat(Position.Z);
            WriteInt(Lod);
            return bytes.ToArray();
        }
    }
}
