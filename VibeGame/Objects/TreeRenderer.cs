using System;
using System.Collections.Generic;
using System.Numerics;
using VibeGame.Core;
using VibeGame.Core.WorldObjects;
using ZeroElectric.Vinculum;
using VibeGame.Terrain;

namespace VibeGame.Objects
{
    public class TreeRenderer : ITreeRenderer, IDisposable
    {
        private readonly ITreesRegistry _treesRegistry;
        private readonly Dictionary<string, List<(Model model, float weight)>> _modelsById = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _loadAttempted = new(StringComparer.OrdinalIgnoreCase);

        public TreeRenderer(ITreesRegistry treesRegistry) => _treesRegistry = treesRegistry;

        public List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> GenerateTrees(
            ITerrainGenerator terrain,
            float[,] heights,
            Vector2 originWorld,
            int count)
        {
            var list = new List<(Vector3, float, float, float)>();
            int size = heights.GetLength(0);
            float chunkWorld = (size - 1) * terrain.TileSize;
            float margin = MathF.Max(2f * terrain.TileSize, 3f);

            for (int i = 0; i < count; i++)
            {
                float wx = HashToRange(i * 13 + 1, originWorld.X + margin, originWorld.X + chunkWorld - margin);
                float wz = HashToRange(i * 29 + 3, originWorld.Y + margin, originWorld.Y + chunkWorld - margin);
                float baseY = terrain.ComputeHeight(wx, wz);

                float s = 1.5f;
                float slope = MathF.Max(
                    MathF.Abs(terrain.ComputeHeight(wx + s, wz) - baseY),
                    MathF.Abs(terrain.ComputeHeight(wx - s, wz) - baseY));
                slope = MathF.Max(slope, MathF.Max(
                    MathF.Abs(terrain.ComputeHeight(wx, wz + s) - baseY),
                    MathF.Abs(terrain.ComputeHeight(wx, wz - s) - baseY)));
                if (slope > 1.8f) continue;

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
            EnsureModelsForIdLoaded(treeId);

            if (!_modelsById.TryGetValue(treeId, out var entries) || entries.Count == 0) return;

            int seed = HashCode.Combine(treeId.GetHashCode(StringComparison.OrdinalIgnoreCase), (int)pos.X, (int)pos.Z);
            seed = Math.Abs(seed);
            float t = (seed % 10000) / 10000f;

            float totalW = 0f;
            foreach (var e in entries) totalW += MathF.Max(0.0001f, e.weight);

            float accum = 0f;
            Model model = entries[0].model;
            float rotationDeg = 0f;
            bool randomY = false;

            foreach (var e in entries)
            {
                accum += MathF.Max(0.0001f, e.weight) / totalW;
                if (t <= accum)
                {
                    model = e.model;
                    // If we stored rotation and randomY with model metadata, read them here
                    // For now, backward compatible defaults:
                    rotationDeg = 0f;
                    randomY = true;
                    break;
                }
            }

            float scale = MathF.Max(24.0f, trunkHeight * 16.0f);
            var bbox = Raylib.GetModelBoundingBox(model);

            // GLB correction (Z-up)
            bool isGlb = true; // always apply correction for GLB
            Quaternion qCorrection = isGlb
                ? Quaternion.CreateFromAxisAngle(new Vector3(1, 0, 0), -MathF.PI / 2f)
                : Quaternion.Identity;

            // Apply user rotation from JSON
            Quaternion qRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rotationDeg * (MathF.PI / 180f));

            // Apply random Y rotation if enabled
            if (randomY)
            {
                float rndDeg = HashToRange(seed * 7 + 5, 0f, 360f);
                Quaternion qRandomY = Quaternion.CreateFromAxisAngle(Vector3.UnitY, rndDeg * (MathF.PI / 180f));
                qRotation = Quaternion.Concatenate(qRotation, qRandomY);
            }

            // Compute up-axis rotation
            Quaternion qUp = ChooseUpAxisByExtents(bbox, new Vector3(scale), preferYUp: true);

            // Combine rotations: user rotation -> GLB correction -> auto up
            Quaternion qFinal = Quaternion.Normalize(Quaternion.Concatenate(qUp, Quaternion.Concatenate(qCorrection, qRotation)));

            // Convert to axis-angle
            ToAxisAngle(qFinal, out Vector3 axis, out float angleDegrees);

            float baseOffset = -EvalYExtent(bbox, new Vector3(scale), qFinal, out float minY);

            Vector3 modelPos = new(pos.X, pos.Y + baseOffset, pos.Z);
            Raylib.DrawModelEx(model, modelPos, axis, angleDegrees, new Vector3(scale), Raylib.WHITE);
        }


        private void EnsureModelsForIdLoaded(string treeId)
        {
            if (_loadAttempted.Contains(treeId)) return;
            _loadAttempted.Add(treeId);

            if (!_treesRegistry.TryGet(treeId, out var def)) return;

            var list = new List<(Model, float)>();
            try
            {
                if (def.Assets?.Models != null)
                {
                    foreach (var m in def.Assets.Models)
                    {
                        if (string.IsNullOrWhiteSpace(m.Path)) continue;
                        try
                        {
                            string ext = System.IO.Path.GetExtension(m.Path);
                            if (ext.Equals(".glb", StringComparison.OrdinalIgnoreCase) ||
                                ext.Equals(".gltf", StringComparison.OrdinalIgnoreCase))
                            {
                                var loaded = RaylibGLBLoader.LoadGLB(m.Path);
                                float per = (m.Weight <= 0f ? 1f : m.Weight) / loaded.Count;
                                foreach (var mdl in loaded) list.Add((mdl, per));
                                continue;
                            }
                            list.Add((Raylib.LoadModel(m.Path), m.Weight <= 0f ? 1f : m.Weight));
                        }
                        catch
                        {
                            /* skip */
                        }
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            if (list.Count > 0) _modelsById[treeId] = list;
        }

        public void Dispose()
        {
            foreach (var kv in _modelsById)
            foreach (var (model, _) in kv.Value)
                try { Raylib.UnloadModel(model); }
                catch {}
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
                return min + (max - min) * ((x % 10000) / 10000f);
            }
        }

        private static void ToAxisAngle(Quaternion q, out Vector3 axis, out float angleDegrees)
        {
            q = Quaternion.Normalize(q);
            float angle = 2.0f * MathF.Acos(Math.Clamp(q.W, -1f, 1f));
            float s = MathF.Sqrt(1f - q.W * q.W);
            axis = s < 0.001f ? new Vector3(1, 0, 0) : new Vector3(q.X / s, q.Y / s, q.Z / s);
            angleDegrees = angle * (180f / MathF.PI);
        }

        private static Quaternion ChooseUpAxisByExtents(BoundingBox bbox, Vector3 scale, bool preferYUp)
        {
            var qY = Quaternion.Identity;
            var qZ = Quaternion.CreateFromAxisAngle(new Vector3(1f, 0f, 0f), -MathF.PI / 2f);

            float eY = EvalYExtent(bbox, scale, qY, out _);
            float eZ = EvalYExtent(bbox, scale, qZ, out _);

            const float bias = 1.1f;
            if (eZ > eY * bias) return qZ;
            if (eY > eZ * bias) return qY;
            return preferYUp ? qY : qZ;
        }

        private static float EvalYExtent(BoundingBox bbox, Vector3 scale, Quaternion q, out float minY)
        {
            Vector3 min = bbox.min, max = bbox.max;
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
                new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
                new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
                new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z)
            };

            float minVal = float.PositiveInfinity, maxVal = float.NegativeInfinity;
            foreach (var c in corners)
            {
                Vector3 r = Vector3.Transform(new Vector3(c.X * scale.X, c.Y * scale.Y, c.Z * scale.Z), q);
                minVal = MathF.Min(minVal, r.Y);
                maxVal = MathF.Max(maxVal, r.Y);
            }

            minY = float.IsFinite(minVal) ? minVal : 0f;
            return float.IsFinite(maxVal) ? maxVal - minY : 0f;
        }
    }
}
