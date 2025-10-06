using System.Numerics;
using Raylib_CsLo;
using VibeGame.Terrain;
using VibeGame.Core.WorldObjects;

namespace VibeGame.Objects
{
    public class TreeRenderer : ITreeRenderer, IDisposable
    {
        private readonly List<Model> _models = new();
        private bool _modelsLoaded;

        private readonly ITreesRegistry _treesRegistry;
        // Cache of loaded models per tree id with configured weights
        private readonly Dictionary<string, List<(Model model, float weight)>> _modelsById = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loadAttempted = new(StringComparer.OrdinalIgnoreCase);

        public TreeRenderer(ITreesRegistry treesRegistry)
        {
            _treesRegistry = treesRegistry;
        }

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

        public void DrawTree(string treeId, Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)
        {
            if (string.IsNullOrWhiteSpace(treeId)) return;

            // Ensure models for this id are loaded from TreesRegistry
            EnsureModelsForIdLoaded(treeId);
            if (!_modelsById.TryGetValue(treeId, out var entries) || entries.Count == 0)
            {
                // No models configured for this id; respect config by drawing nothing
                return;
            }

            // Deterministically select a model based on position and treeId using weights
            int seed = HashCode.Combine(treeId.GetHashCode(StringComparison.OrdinalIgnoreCase), (int)pos.X, (int)pos.Z);
            if (seed < 0) seed = -seed;
            float t = ((uint)seed % 10000) / 10000f; // [0,1)

            float totalW = 0f;
            for (int i = 0; i < entries.Count; i++) totalW += MathF.Max(0.0001f, entries[i].weight);
            float accum = 0f;
            Model model = entries[0].model;
            for (int i = 0; i < entries.Count; i++)
            {
                float w = MathF.Max(0.0001f, entries[i].weight) / totalW;
                accum += w;
                if (t <= accum)
                {
                    model = entries[i].model;
                    break;
                }
            }

            // Scale based on desired trunk height; models come at varying unit scales
            float scale = MathF.Max(24.0f, trunkHeight * 16.0f);

            // Align model base to the ground; many assets are Z-up, rotate -90deg around X
            var bbox = Raylib.GetModelBoundingBox(model);
            float baseOffset = -bbox.min.Z * scale;
            Vector3 modelPos = new Vector3(pos.X, pos.Y + baseOffset, pos.Z);

            Raylib.DrawModelEx(model, modelPos, new Vector3(1, 0, 0), -90f, new Vector3(scale, scale, scale), Raylib.WHITE);
        }

        private void EnsureModelsLoaded()
        {
            if (_modelsLoaded) return;
            _modelsLoaded = true; // avoid reentry
            try
            {
                string baseDir = AppContext.BaseDirectory;

                // Preferred location in this project
                string[] candidateDirs = new[]
                {
                    Path.Combine(baseDir, "assets", "models", "world", "trees"),
                    // Backward-compatible legacy location
                    Path.Combine(baseDir, "assets", "trees")
                };

                foreach (var dir in candidateDirs)
                {
                    if (!Directory.Exists(dir)) continue;
                    var glbFiles = Array.Empty<string>();
                    try { glbFiles = Directory.GetFiles(dir, "*.glb"); } catch { /* ignore */ }
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
                    // If we loaded any from preferred dir, no need to scan others
                    if (_models.Count > 0) break;
                }
            }
            catch
            {
                // Swallow: we intentionally avoid fallback drawing when models are missing
            }
        }

        private void EnsureModelsForIdLoaded(string treeId)
        {
            if (string.IsNullOrWhiteSpace(treeId)) return;
            if (_loadAttempted.Contains(treeId)) return;
            _loadAttempted.Add(treeId);

            if (!_treesRegistry.TryGet(treeId, out var def)) return;
            var list = new List<(Model model, float weight)>();
            try
            {
                if (def.Assets != null && def.Assets.Models != null)
                {
                    foreach (var m in def.Assets.Models)
                    {
                        if (string.IsNullOrWhiteSpace(m.Path)) continue;
                        try
                        {
                            var model = Raylib.LoadModel(m.Path);
                            float w = m.Weight <= 0f ? 1f : m.Weight;
                            list.Add((model, w));
                        }
                        catch
                        {
                            // skip this asset
                        }
                    }
                }
            }
            catch
            {
                // ignore load errors; we will simply not render this id
            }

            if (list.Count > 0)
            {
                _modelsById[treeId] = list;
            }
        }

        public void Dispose()
        {
            // Unload generic models if any were loaded
            foreach (var m in _models)
            {
                try { Raylib.UnloadModel(m); } catch { /* ignore */ }
            }
            _models.Clear();

            // Unload per-id models
            foreach (var kv in _modelsById)
            {
                var list = kv.Value;
                foreach (var (model, _) in list)
                {
                    try { Raylib.UnloadModel(model); } catch { /* ignore */ }
                }
            }
            _modelsById.Clear();
            _loadAttempted.Clear();
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
