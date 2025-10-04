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
            int count)
        {
            var list = new List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)>();

            // Constrain tree placement to terrain bounds to avoid edge clamping that forms visible lines
            float half = (terrain.TerrainSize - 2) * terrain.TileSize * 0.5f; // keep 1 tile margin each side
            float margin = MathF.Max(2f * terrain.TileSize, 3f);
            float minX = -half + margin;
            float maxX = half - margin;
            float minZ = -half + margin;
            float maxZ = half - margin;

            for (int i = 0; i < count; i++)
            {
                float rx = HashToRange(i * 13 + 1, minX, maxX);
                float rz = HashToRange(i * 29 + 3, minZ, maxZ);
                float baseY = terrain.SampleHeight(heights, rx, rz);

                // Skip very steep areas by sampling nearby heights
                float ny1 = terrain.SampleHeight(heights, rx + 1.5f, rz);
                float ny2 = terrain.SampleHeight(heights, rx - 1.5f, rz);
                float ny3 = terrain.SampleHeight(heights, rx, rz + 1.5f);
                float ny4 = terrain.SampleHeight(heights, rx, rz - 1.5f);
                float slope = MathF.Max(MathF.Max(MathF.Abs(ny1 - baseY), MathF.Abs(ny2 - baseY)), MathF.Max(MathF.Abs(ny3 - baseY), MathF.Abs(ny4 - baseY)));
                if (slope > 1.8f) continue; // avoid extreme slopes

                float trunkHeight = 2.0f + HashToRange(i * 17 + 7, 0.5f, 3.5f);
                float trunkRadius = 0.25f + HashToRange(i * 31 + 9, -0.05f, 0.15f);
                float canopyRadius = trunkHeight * HashToRange(i * 47 + 13, 0.45f, 0.65f);
                list.Add((new Vector3(rx, baseY, rz), trunkHeight, trunkRadius, canopyRadius));
            }
            return list;
        }

        public void DrawTree(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)
        {
            EnsureModelsLoaded();
            if (_models.Count == 0) return; // no fallback drawing

            // Deterministically select a model based on position
            int seed = (int)(pos.X * 73856093) ^ (int)(pos.Z * 19349663);
            if (seed < 0) seed = -seed;
            int index = seed % _models.Count;
            var model = _models[index];

            // Scale model roughly to the generated parameters
            float scale = MathF.Max(0.6f, trunkHeight / 3.0f);

            // Draw model at ground position
            Raylib.DrawModel(model, pos, scale, Raylib.WHITE);
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
