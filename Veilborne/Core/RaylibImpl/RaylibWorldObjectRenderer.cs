using System;
using System.Collections.Generic;
using System.Numerics;
using Veilborne.Core;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using ZeroElectric.Vinculum;
using Veilborne.Interfaces;
using Veilborne.Objects;

namespace Veilborne.Core.RaylibImpl
{
    public sealed class RaylibWorldObjectRenderer : IWorldObjectRenderer, IRenderSystem, IDisposable
    {
        private readonly Dictionary<string, Model> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly EntityRegistry _entities;

        public RaylibWorldObjectRenderer(EntityRegistry entities)
        {
            _entities = entities;
        }

        public void Draw()
        {
            foreach (var entity in _entities.GetEntitiesWith<RenderComponent>())
            {
                if (entity.TryGetComponent<TransformComponent>(out var transform) &&
                    entity.TryGetComponent<RenderComponent>(out var render))
                {
                    if (!render.Visible) continue;
                    DrawInternal(transform.Position, transform.Rotation, transform.Scale, render.ModelPath, render.ConfigRotationDegrees);
                }
            }
        }

        public void DrawWorldObject(SpawnedObject obj)
        {
            if (obj == null) return;
            DrawInternal(obj.Position, obj.Rotation, obj.Scale, obj.ModelPath, obj.ConfigRotationDegrees);
        }

        private void DrawInternal(Vector3 position, Quaternion rotation, Vector3 scale, string modelPath, float? configRotationDegrees)
        {
            if (string.IsNullOrWhiteSpace(modelPath)) return;
            if (!_cache.TryGetValue(modelPath, out var model))
            {
                try { model = Raylib.LoadModel(modelPath); _cache[modelPath] = model; }
                catch { return; }
            }

            var bbox = Raylib.GetModelBoundingBox(model);

            Quaternion qFinal;
            {
                Quaternion qCorrection = ChooseUpAxisByExtents(bbox, scale, preferYUp: true);
                if (configRotationDegrees.HasValue)
                    qFinal = Quaternion.Normalize(Quaternion.Concatenate(rotation, qCorrection));
                else
                    qFinal = Quaternion.Normalize(qCorrection);
            }

            ToAxisAngle(qFinal, out Vector3 axis, out float angleDegrees);

            float baseOffset = ComputeBaseLift(bbox, scale, qFinal);

            Vector3 modelPos = new(position.X, position.Y + baseOffset, position.Z);

            Raylib.DrawModelEx(model, modelPos, axis, angleDegrees, scale, Raylib.WHITE);
        }



        public void Dispose()
        {
            foreach (var kv in _cache)
                try { Raylib.UnloadModel(kv.Value); } catch { }
            _cache.Clear();
        }

        private static void ToAxisAngle(Quaternion q, out Vector3 axis, out float angleDegrees)
        {
            if (q.W > 1f || q.W < -1f) q = Quaternion.Normalize(q);
            float angle = 2.0f * MathF.Acos(q.W);
            float s = MathF.Sqrt(1.0f - q.W * q.W);
            axis = s < 0.001f ? new Vector3(1, 0, 0) : new Vector3(q.X / s, q.Y / s, q.Z / s);
            angleDegrees = angle * (180f / MathF.PI);
        }


        private static Quaternion ChooseUpAxisByExtents(BoundingBox bbox, Vector3 scale, bool preferYUp)
        {
            // Candidates:
            // qY: identity (already Y-up)
            // qZUp: -90° around X to convert Z-up → Y-up (common for GLB)
            // qXUp: +90° around Z to convert X-up → Y-up (some exporters)
            var qY = Quaternion.Identity;
            var qZUp = Quaternion.CreateFromAxisAngle(new Vector3(1f, 0f, 0f), -MathF.PI / 2f);
            var qXUp = Quaternion.CreateFromAxisAngle(new Vector3(0f, 0f, 1f), +MathF.PI / 2f);

            float eY = EvalYExtent(bbox, scale, qY, out _);
            float eZ = EvalYExtent(bbox, scale, qZUp, out _);
            float eX = EvalYExtent(bbox, scale, qXUp, out _);

            // Pick the orientation that yields the largest Y extent, with a slight bias
            const float bias = 1.1f;
            float best = eY; var qBest = qY;
            if (eZ > best * (qBest == qY ? bias : 1f)) { best = eZ; qBest = qZUp; }
            if (eX > best * (qBest == qY ? bias : 1f)) { best = eX; qBest = qXUp; }

            // If ambiguous, prefer Y-up if requested; otherwise favor Z-up correction by default
            if (qBest == qY && MathF.Abs(eZ - eY) < 1e-3f && MathF.Abs(eX - eY) < 1e-3f)
                return preferYUp ? qY : qZUp;
            return qBest;
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

        private static float ComputeBaseLift(BoundingBox bbox, Vector3 scale, Quaternion q)
        {
            EvalYExtent(bbox, scale, q, out float minY);
            return -minY;
        }
    }
}
