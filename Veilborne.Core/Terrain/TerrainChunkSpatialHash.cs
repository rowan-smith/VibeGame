using System.Collections.Concurrent;

namespace Veilborne.Terrain
{
    internal static class TerrainChunkSpatialHash
    {
        private static readonly ConcurrentDictionary<int, (int dx, int dz)[]> RadiusOffsets = new();

        public static (int cx, int cz)[] GetChunksAround(int centerX, int centerZ, int radius)
        {
            var offsets = RadiusOffsets.GetOrAdd(radius, BuildOffsets);
            var result = new (int cx, int cz)[offsets.Length];
            for (int i = 0; i < offsets.Length; i++)
                result[i] = (centerX + offsets[i].dx, centerZ + offsets[i].dz);
            return result;
        }

        private static (int dx, int dz)[] BuildOffsets(int radius)
        {
            var list = new List<(int dx, int dz)>((radius * 2 + 1) * (radius * 2 + 1));
            for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
                list.Add((x, z));
            return list.ToArray();
        }
    }
}
