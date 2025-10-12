using System.Numerics;
using ZeroElectric.Vinculum;

namespace VibeGame.Objects
{
    public sealed class WorldObjectRenderer : IWorldObjectRenderer, System.IDisposable
    {
        private readonly Dictionary<string, Model> _cache = new(StringComparer.OrdinalIgnoreCase);

        public void DrawWorldObject(SpawnedObject obj)
        {
            if (obj == null || string.IsNullOrWhiteSpace(obj.ModelPath)) return;

            var model = Load(obj.ModelPath);
            if (model == null) return;

            // Compose rotations so the asset-up correction is applied first, then the object's yaw.
            // Assets are Z-up; rotate +90deg around X to make them Y-up, then apply the object's rotation
            // around world Y. Using Concatenate(qCorrection, obj.Rotation) ensures the yaw happens in world-up space.
            var qCorrection = Quaternion.CreateFromAxisAngle(new Vector3(1f, 0f, 0f), MathF.PI / 2f); // +90deg X
            var qFinal = Quaternion.Normalize(Quaternion.Concatenate(qCorrection, obj.Rotation));

            // Convert to axis-angle for Raylib.DrawModelEx
            ToAxisAngle(qFinal, out Vector3 axis, out float angleDegrees);

            // Align the model's base to the terrain based on the model's Z axis (assets are Z-up before correction).
            // After applying the +90deg X correction, model Z maps to world Y; lift by -min.Z so the lowest point touches ground.
            var bbox = Raylib.GetModelBoundingBox(model.Value);
            float baseOffset = ComputeBaseLift(bbox, obj.Scale, qCorrection);
            Vector3 modelPos = new Vector3(obj.Position.X, obj.Position.Y + baseOffset, obj.Position.Z);

            Raylib.DrawModelEx(model.Value, modelPos, axis, angleDegrees, obj.Scale, Raylib.WHITE);
        }

        private Model? Load(string path)
        {
            if (_cache.TryGetValue(path, out var m)) return m;
            try
            {
                var model = Raylib.LoadModel(path);
                _cache[path] = model;
                return model;
            }
            catch
            {
                return null;
            }
        }

        private static void ToAxisAngle(Quaternion q, out Vector3 axis, out float angleDegrees)
        {
            if (q.W > 1f || q.W < -1f) q = Quaternion.Normalize(q);
            float angle = 2.0f * MathF.Acos(q.W);
            float s = MathF.Sqrt(1.0f - q.W * q.W);
            if (s < 0.001f)
            {
                axis = new Vector3(1, 0, 0);
            }
            else
            {
                axis = new Vector3(q.X / s, q.Y / s, q.Z / s);
            }
            angleDegrees = angle * (180f / MathF.PI);
        }

        private static float ComputeBaseLift(BoundingBox bbox, Vector3 scale, Quaternion qCorrection)
        {
            // Compute the lowest Y after applying scale and the fixed asset-up correction.
            // Yaw around world Y is ignored here because it does not affect vertical extents.
            Vector3 min = bbox.min;
            Vector3 max = bbox.max;
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new Vector3(min.X, min.Y, min.Z),
                new Vector3(max.X, min.Y, min.Z),
                new Vector3(min.X, max.Y, min.Z),
                new Vector3(max.X, max.Y, min.Z),
                new Vector3(min.X, min.Y, max.Z),
                new Vector3(max.X, min.Y, max.Z),
                new Vector3(min.X, max.Y, max.Z),
                new Vector3(max.X, max.Y, max.Z)
            };

            float minY = float.PositiveInfinity;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 c = corners[i];
                // Apply per-axis scale first (as DrawModelEx does), then the axis correction rotation
                Vector3 scaled = new Vector3(c.X * scale.X, c.Y * scale.Y, c.Z * scale.Z);
                Vector3 rotated = Vector3.Transform(scaled, qCorrection);
                if (rotated.Y < minY) minY = rotated.Y;
            }

            if (float.IsInfinity(minY) || float.IsNaN(minY)) minY = 0f;
            return -minY; // lift so the lowest point sits at ground level
        }

        public void Dispose()
        {
            foreach (var kv in _cache)
            {
                try { Raylib.UnloadModel(kv.Value); } catch { }
            }
            _cache.Clear();
        }
    }
}