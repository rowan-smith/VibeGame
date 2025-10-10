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

            // Align model base using its bounding box. With +90deg X, Y maps to -Z, so the
            // lowest point in Y corresponds to -max.Z; to lift base to ground, offset by +max.Z
            var bbox = Raylib.GetModelBoundingBox(model.Value);
            float baseOffset = bbox.max.Z * obj.Scale.Y; // using Y scale as height scale
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