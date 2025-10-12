using System.Numerics;
using ZeroElectric.Vinculum;

namespace VibeGame.Core
{
    public static class ModelUtils
    {
        /// <summary>
        /// Computes the rotation correction for a model so that it is Y-up.
        /// Automatically applies -90° X rotation for GLB/GLTF models.
        /// For other models, uses bounding box extents to pick best axis.
        /// </summary>
        public static void ComputeUpRotation(BoundingBox bbox, Vector3 scale, bool isGlb, out Vector3 axis, out float angleDegrees)
        {
            if (isGlb)
            {
                axis = new Vector3(1, 0, 0);
                angleDegrees = -90f;
            }
            else
            {
                Quaternion qCorrection = ChooseUpAxisByExtents(bbox, scale, preferYUp: true);
                ToAxisAngle(qCorrection, out axis, out angleDegrees);
            }
        }

        public static float EvalYExtent(BoundingBox bbox, Vector3 scale, Quaternion q, out float minY)
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

        private static void ToAxisAngle(Quaternion q, out Vector3 axis, out float angleDegrees)
        {
            if (q.W > 1f || q.W < -1f) q = Quaternion.Normalize(q);
            float angle = 2.0f * MathF.Acos(q.W);
            float s = MathF.Sqrt(1.0f - q.W * q.W);
            axis = s < 0.001f ? new Vector3(1, 0, 0) : new Vector3(q.X / s, q.Y / s, q.Z / s);
            angleDegrees = angle * (180f / MathF.PI);
        }
    }
}
