using System.Numerics;
using Raylib_CsLo;
using VibeGame.Terrain;

namespace VibeGame.Objects
{
    public class TreeRenderer : ITreeRenderer, IDisposable
    {
        private readonly List<Model> _models = new();
        private bool _modelsLoaded;

        public List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain,
            float[,] heights,
            Vector2 originWorld,
            int count)
        {
            var list = new List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)>();

            // Chunk world size in meters (covers (size-1) tiles)
            int size = heights.GetLength(0);
            float chunkWorldSize = (size - 1) * terrain.TileSize;

            // Keep a small margin from chunk edges
            float margin = MathF.Max(2f * terrain.TileSize, 3f);
            float minX = originWorld.X + margin;
            float maxX = originWorld.X + chunkWorldSize - margin;
            float minZ = originWorld.Y + margin;
            float maxZ = originWorld.Y + chunkWorldSize - margin;

            for (int i = 0; i < count; i++)
            {
                float wx = HashToRange(i * 13 + 1, minX, maxX);
                float wz = HashToRange(i * 29 + 3, minZ, maxZ);

                // Base height from infinite terrain function in world coordinates
                float baseY = terrain.ComputeHeight(wx, wz);

                // Skip very steep areas by sampling nearby world positions
                float s = 1.5f;
                float ny1 = terrain.ComputeHeight(wx + s, wz);
                float ny2 = terrain.ComputeHeight(wx - s, wz);
                float ny3 = terrain.ComputeHeight(wx, wz + s);
                float ny4 = terrain.ComputeHeight(wx, wz - s);
                float slope = MathF.Max(MathF.Max(MathF.Abs(ny1 - baseY), MathF.Abs(ny2 - baseY)), MathF.Max(MathF.Abs(ny3 - baseY), MathF.Abs(ny4 - baseY)));
                if (slope > 1.8f) continue; // avoid extreme slopes

                float trunkHeight = 2.0f + HashToRange(i * 17 + 7, 0.5f, 3.5f);
                float trunkRadius = 0.25f + HashToRange(i * 31 + 9, -0.05f, 0.15f);
                float canopyRadius = trunkHeight * HashToRange(i * 47 + 13, 0.45f, 0.65f);
                list.Add((new Vector3(wx, baseY, wz), trunkHeight, trunkRadius, canopyRadius));
            }
            return list;
        }

        public void DrawTree(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)
        {
            EnsureModelsLoaded();
            if (_models.Count == 0)
            {
                // Fallback: draw a simple procedural tree so something is always visible
                // Draw trunk as a cylinder standing on the ground
                Vector3 trunkPos = new Vector3(pos.X, pos.Y + trunkHeight * 0.5f, pos.Z);
                Raylib.DrawCylinder(trunkPos, trunkRadius, trunkRadius * 0.9f, trunkHeight, 12, new Color(110, 82, 54, 255));
                // Draw canopy as a green sphere
                Vector3 canopyPos = new Vector3(pos.X, pos.Y + trunkHeight + canopyRadius * 0.8f, pos.Z);
                Raylib.DrawSphere(canopyPos, canopyRadius, new Color(34, 139, 34, 255));
                return;
            }

            // Deterministically select a model based on position
            int seed = (int)(pos.X * 73856093) ^ (int)(pos.Z * 19349663);
            if (seed < 0) seed = -seed;
            int index = seed % _models.Count;
            var model = _models[index];

            // Scale up for visibility, based on desired trunk height
            // Models ship in various unit scales. Make them significantly larger by default.
            // Previous: max(6.0, trunkHeight * 3.5)
            float scale = MathF.Max(24.0f, trunkHeight * 16.0f);

            // Offset model so its bottom sits on terrain at pos.Y. We rotate models upright (see DrawModelEx below).
            // Many tree assets are Z-up; after a -90deg rotation around X, original Z becomes Y.
            var bbox = Raylib.GetModelBoundingBox(model);
            float baseOffset = -bbox.min.Z * scale; // align base to ground post-rotation
            Vector3 modelPos = new Vector3(pos.X, pos.Y + baseOffset, pos.Z);

            // Draw model upright (rotate -90 degrees about X so Z-up assets stand vertically)
            Raylib.DrawModelEx(model, modelPos, new Vector3(1, 0, 0), -90f, new Vector3(scale, scale, scale), Raylib.WHITE);
        }

        private void EnsureModelsLoaded()
        {
            if (_modelsLoaded) return;
            _modelsLoaded = true; // avoid reentry
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string treesDir = Path.Combine(baseDir, "assets", "trees");
                if (Directory.Exists(treesDir))
                {
                    // Prefer only GLB (self-contained) to avoid native loader crashes with external-buffer GLTF
                    var glbFiles = Directory.GetFiles(treesDir, "*.glb");
                    if (glbFiles.Length > 0)
                    {
                        foreach (var f in glbFiles)
                        {
                            try
                            {
                                var model = Raylib.LoadModel(f);
                                _models.Add(model);
                            }
                            catch
                            {
                                // skip invalid model; continue
                            }
                        }
                    }
                }
            }
            catch
            {
                // swallow; no fallback drawing
            }
        }

        public void Dispose()
        {
            foreach (var m in _models)
            {
                try { Raylib.UnloadModel(m); } catch { /* ignore */ }
            }
            _models.Clear();
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
