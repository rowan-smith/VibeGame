using System.Numerics;
using VibeGame.Terrain;

namespace VibeGame.Biomes.Spawners
{
    public class PineTreeSpawner : ITreeSpawner
    {
        public List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain,
            Vector2 originWorld,
            int chunkSize,
            int targetCount)
        {
            var list = new List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)>();
            float tile = terrain.TileSize;
            float chunkWorldSize = (chunkSize - 1) * tile;

            float margin = MathF.Max(2f * tile, 3f);
            float minX = originWorld.X + margin;
            float maxX = originWorld.X + chunkWorldSize - margin;
            float minZ = originWorld.Y + margin;
            float maxZ = originWorld.Y + chunkWorldSize - margin;

            for (int i = 0; i < targetCount; i++)
            {
                float wx = HashToRange(i * 97 + 5, minX, maxX);
                float wz = HashToRange(i * 211 + 23, minZ, maxZ);

                float baseY = terrain.ComputeHeight(wx, wz);

                // Avoid steep slopes (pines tolerate a bit more)
                float s = 1.5f;
                float ny1 = terrain.ComputeHeight(wx + s, wz);
                float ny2 = terrain.ComputeHeight(wx - s, wz);
                float ny3 = terrain.ComputeHeight(wx, wz + s);
                float ny4 = terrain.ComputeHeight(wx, wz - s);
                float slope = MathF.Max(MathF.Max(MathF.Abs(ny1 - baseY), MathF.Abs(ny2 - baseY)), MathF.Max(MathF.Abs(ny3 - baseY), MathF.Abs(ny4 - baseY)));
                if (slope > 2.2f) continue;

                // Pine: taller and slightly thicker, narrower canopy
                float trunkHeight = 3.0f + HashToRange(i * 17 + 11, 1.4f, 4.5f);
                float trunkRadius = 0.25f + HashToRange(i * 37 + 19, -0.02f, 0.18f);
                float canopyRadius = trunkHeight * HashToRange(i * 41 + 29, 0.35f, 0.55f);
                list.Add((new Vector3(wx, baseY, wz), trunkHeight, trunkRadius, canopyRadius));
            }

            return list;
        }

        private static float HashToRange(int seed, float min, float max)
        {
            unchecked
            {
                uint x = (uint)seed;
                x ^= x << 13;
                x ^= x >> 17;
                x ^= x << 5;
                float t = (x % 10000) / 10000f;
                return min + (max - min) * t;
            }
        }
    }
}
