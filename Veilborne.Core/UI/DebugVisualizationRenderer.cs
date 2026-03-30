using System.Numerics;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core.UI
{
    /// <summary>
    /// Renders 3D debug overlays projected onto the 2D screen:
    /// chunk boundary wireframes and collider radii circles.
    /// </summary>
    public sealed class DebugVisualizationRenderer
    {
        private readonly IInfiniteTerrain _terrain;
        private readonly EntityRegistry _entities;

        private IUiProvider _ui = null!;
        private IGraphicsProvider _graphics = null!;

        public DebugVisualizationRenderer(IInfiniteTerrain terrain, EntityRegistry entities)
        {
            _terrain = terrain;
            _entities = entities;
        }

        public void Initialize(IUiProvider ui, IGraphicsProvider graphics)
        {
            _ui = ui;
            _graphics = graphics;
        }

        // ── Chunk bounds ───────────────────────────────────────────

        public void DrawChunkBoundsOverlay(CameraComponent camera)
        {
            if (_terrain is not IDebugTerrain debugTerrain) return;

            var info = debugTerrain.GetDebugInfo(camera.Position);
            float chunkWorld = info.ChunkSize * info.TileSize;
            float chunkMinX = info.ChunkX * chunkWorld;
            float chunkMinZ = info.ChunkZ * chunkWorld;
            float chunkMaxX = chunkMinX + chunkWorld;
            float chunkMaxZ = chunkMinZ + chunkWorld;

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            const int samplesPerAxis = 5;
            for (int z = 0; z < samplesPerAxis; z++)
            for (int x = 0; x < samplesPerAxis; x++)
            {
                float tx = x / (float)(samplesPerAxis - 1);
                float tz = z / (float)(samplesPerAxis - 1);
                float wx = chunkMinX + tx * chunkWorld;
                float wz = chunkMinZ + tz * chunkWorld;
                float wy = _terrain.SampleHeight(new Vector3(wx, 0f, wz));
                if (wy < minY) minY = wy;
                if (wy > maxY) maxY = wy;
            }

            if (!float.IsFinite(minY) || !float.IsFinite(maxY))
                return;

            float midX = (chunkMinX + chunkMaxX) * 0.5f;
            float midZ = (chunkMinZ + chunkMaxZ) * 0.5f;
            const float groundOffset = 0.12f;
            int segments = Math.Max(4, info.ChunkSize / 2);

            var groundColor = new Vector4(0.2f, 0.95f, 0.2f, 1f);
            DrawTerrainPolyline(new Vector3(chunkMinX, 0f, chunkMinZ), new Vector3(chunkMaxX, 0f, chunkMinZ), segments, groundOffset, groundColor, camera);
            DrawTerrainPolyline(new Vector3(chunkMaxX, 0f, chunkMinZ), new Vector3(chunkMaxX, 0f, chunkMaxZ), segments, groundOffset, groundColor, camera);
            DrawTerrainPolyline(new Vector3(chunkMaxX, 0f, chunkMaxZ), new Vector3(chunkMinX, 0f, chunkMaxZ), segments, groundOffset, groundColor, camera);
            DrawTerrainPolyline(new Vector3(chunkMinX, 0f, chunkMaxZ), new Vector3(chunkMinX, 0f, chunkMinZ), segments, groundOffset, groundColor, camera);

            var crossColor = new Vector4(0.7f, 0.95f, 0.2f, 1f);
            DrawTerrainPolyline(new Vector3(chunkMinX, 0f, midZ), new Vector3(chunkMaxX, 0f, midZ), segments, groundOffset, crossColor, camera);
            DrawTerrainPolyline(new Vector3(midX, 0f, chunkMinZ), new Vector3(midX, 0f, chunkMaxZ), segments, groundOffset, crossColor, camera);

            float skyTop = MathF.Max(maxY + 35f, camera.Position.Y + 25f);
            var skyColor = new Vector4(0.35f, 0.95f, 1.0f, 0.95f);
            DrawSkyPillar(chunkMinX, chunkMinZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(chunkMaxX, chunkMinZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(chunkMaxX, chunkMaxZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(chunkMinX, chunkMaxZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(midX, midZ, groundOffset, skyTop, new Vector4(0.95f, 0.95f, 0.2f, 0.95f), camera);
        }

        // ── Collider radii ─────────────────────────────────────────

        public void DrawColliderRadiiOverlay(CameraComponent camera)
        {
            foreach (var entity in _entities.GetEntitiesWith<WorldObjectComponent, TransformComponent>())
            {
                if (!entity.TryGetComponent<ColliderComponent>(out var collider) || collider.Radius <= 0.01f)
                    continue;

                var t = entity.GetComponent<TransformComponent>();
                var toCamera = t.Position - camera.Position;
                if (toCamera.LengthSquared() > 120f * 120f)
                    continue;
                if (!TryProjectWorldToScreen(t.Position, camera, out var center2d)) continue;
                if (!TryProjectWorldToScreen(t.Position + Vector3.UnitX * collider.Radius, camera, out var edge2d)) continue;

                float radiusPx = Vector2.Distance(center2d, edge2d);
                if (radiusPx < 2f || radiusPx > 600f) continue;

                var color = new Vector4(1f, 0.42f, 0.2f, 0.9f);
                if (entity.TryGetComponent<CollisionFilterComponent>(out var filter) && filter.Layer == CollisionLayer.Foliage)
                    color = new Vector4(0.2f, 0.9f, 0.3f, 0.9f);

                DrawCircle(center2d, radiusPx, color, 20);
                DrawColliderLabel(entity, center2d, radiusPx, color);
            }
        }

        // ── Terrain-following polyline ─────────────────────────────

        private void DrawTerrainPolyline(Vector3 start, Vector3 end, int segments, float heightOffset,
            Vector4 color, CameraComponent camera)
        {
            int segs = Math.Max(1, segments);
            Vector3 prev = start;
            prev.Y = _terrain.SampleHeight(prev) + heightOffset;

            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs;
                Vector3 cur = Vector3.Lerp(start, end, t);
                cur.Y = _terrain.SampleHeight(cur) + heightOffset;
                DrawProjectedLine(prev, cur, color, camera);
                prev = cur;
            }
        }

        private void DrawSkyPillar(float worldX, float worldZ, float groundOffset, float skyTop,
            Vector4 color, CameraComponent camera)
        {
            float yBase = _terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + groundOffset;
            DrawProjectedLine(new Vector3(worldX, yBase, worldZ), new Vector3(worldX, skyTop, worldZ), color, camera);
        }

        // ── 2D primitives ──────────────────────────────────────────

        private void DrawCircle(Vector2 center, float radius, Vector4 color, int segments)
        {
            if (segments < 6) segments = 6;
            float step = (2f * MathF.PI) / segments;
            Vector2 prev = center + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * step;
                Vector2 next = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                _ui.DrawLine((int)prev.X, (int)prev.Y, (int)next.X, (int)next.Y, color);
                prev = next;
            }
        }

        private void DrawColliderLabel(Entity entity, Vector2 center2d, float radiusPx, Vector4 color)
        {
            if (radiusPx < 14f)
                return;

            string label = GetColliderDebugLabel(entity);
            if (string.IsNullOrWhiteSpace(label))
                return;

            int fontSize = Math.Clamp((int)(radiusPx * 0.16f), 10, 14);
            int textWidth = _ui.MeasureText(label, fontSize);
            if (textWidth <= 0)
                return;

            int lineY = (int)center2d.Y;
            int lineX0 = (int)(center2d.X - radiusPx);
            int lineX1 = (int)(center2d.X + radiusPx);
            _ui.DrawLine(lineX0, lineY, lineX1, lineY, new Vector4(color.X, color.Y, color.Z, 0.45f));

            int textX = (int)(center2d.X - textWidth * 0.5f);
            int textY = (int)(center2d.Y - fontSize * 0.5f);
            const int pad = 3;
            _ui.DrawRectangle(textX - pad, textY - pad, textWidth + pad * 2, fontSize + pad * 2,
                new Vector4(0f, 0f, 0f, 0.55f));
            _ui.DrawText(label, textX, textY, fontSize, new Vector4(1f, 1f, 1f, 0.95f));
        }

        private static string GetColliderDebugLabel(Entity entity)
        {
            string raw = string.Empty;
            if (entity.TryGetComponent<NameComponent>(out var name) && !string.IsNullOrWhiteSpace(name.Value))
                raw = name.Value;
            else if (entity.TryGetComponent<RenderComponent>(out var render) && !string.IsNullOrWhiteSpace(render.ModelPath))
                raw = render.ModelPath;

            if (string.IsNullOrWhiteSpace(raw))
                return "Collider";

            string file = Path.GetFileNameWithoutExtension(
                raw.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(file))
                file = raw;
            file = file.Replace('_', ' ').Trim();
            if (file.Length > 24)
                file = file[..24];
            return file;
        }

        // ── Projection ─────────────────────────────────────────────

        private void DrawProjectedLine(Vector3 aWorld, Vector3 bWorld, Vector4 color, CameraComponent camera)
        {
            if (!TryProjectWorldToScreen(aWorld, camera, out var a)) return;
            if (!TryProjectWorldToScreen(bWorld, camera, out var b)) return;
            _ui.DrawLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, color);
        }

        private bool TryProjectWorldToScreen(Vector3 world, CameraComponent camera, out Vector2 screen)
        {
            var forward = Vector3.Normalize(camera.Target - camera.Position);
            var right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));

            var rel = world - camera.Position;
            float xView = Vector3.Dot(rel, right);
            float yView = Vector3.Dot(rel, up);
            float zView = Vector3.Dot(rel, forward);
            if (zView <= 0.05f)
            {
                screen = Vector2.Zero;
                return false;
            }

            float aspect = Math.Max(1f, _graphics.ScreenWidth) / Math.Max(1f, _graphics.ScreenHeight);
            float fovRad = camera.FovY * (MathF.PI / 180f);
            float tanHalf = MathF.Tan(fovRad * 0.5f);
            if (tanHalf <= 1e-5f)
            {
                screen = Vector2.Zero;
                return false;
            }

            float xNdc = xView / (zView * tanHalf * aspect);
            float yNdc = yView / (zView * tanHalf);
            if (xNdc < -1.5f || xNdc > 1.5f || yNdc < -1.5f || yNdc > 1.5f)
            {
                screen = Vector2.Zero;
                return false;
            }

            float sx = (xNdc * 0.5f + 0.5f) * _graphics.ScreenWidth;
            float sy = (1f - (yNdc * 0.5f + 0.5f)) * _graphics.ScreenHeight;
            screen = new Vector2(sx, sy);
            return true;
        }
    }
}
