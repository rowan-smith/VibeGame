using System.Numerics;
using Veilborne.Ecs.Components;

namespace Veilborne.Terrain
{
    /// <summary>
    /// Cheap visibility tests for terrain rings before calling into the GPU renderer.
    /// </summary>
    public static class TerrainRenderCull
    {
        public static float ResolveRingDrawDistanceScale(float tileSize) =>
            tileSize >= 3.5f ? 0.5f : tileSize >= 1.5f ? 0.8f : 1f;

        /// <summary>
        /// Pull far-ring draw distance in when the camera looks toward the horizon so
        /// huge LOD chunks do not fill the view with grazing-angle overdraw.
        /// </summary>
        public static float ResolveHorizonDrawDistanceScale(
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            float tileSize)
        {
            if (tileSize < 3.5f)
                return 1f;

            float fx = cameraTarget.X - cameraPosition.X;
            float fy = cameraTarget.Y - cameraPosition.Y;
            float fz = cameraTarget.Z - cameraPosition.Z;
            float horiz = MathF.Sqrt(fx * fx + fz * fz);
            if (horiz < 1e-3f)
                return 0.35f;

            float pitch = MathF.Abs(fy) / horiz;
            return Math.Clamp(0.35f + pitch * 0.65f, 0.35f, 1f);
        }

        public static float ResolveEffectiveDrawDistance(
            Vector3 cameraPosition,
            Vector3 cameraTarget,
            float tileSize,
            float maxDrawDistance)
        {
            return maxDrawDistance
                   * ResolveRingDrawDistanceScale(tileSize)
                   * ResolveHorizonDrawDistanceScale(cameraPosition, cameraTarget, tileSize);
        }

        public static bool IsChunkRoughlyVisible(
            CameraComponent camera,
            Vector2 chunkOrigin,
            float tileSize,
            int gridWidth,
            int gridHeight,
            float maxDrawDistance)
        {
            float worldW = MathF.Max(1f, (gridWidth - 1) * tileSize);
            float worldH = MathF.Max(1f, (gridHeight - 1) * tileSize);
            float cx = chunkOrigin.X + worldW * 0.5f;
            float cz = chunkOrigin.Y + worldH * 0.5f;

            var camPos = camera.Position;
            float dx = cx - camPos.X;
            float dz = cz - camPos.Z;

            float effectiveDraw = ResolveEffectiveDrawDistance(
                camPos, camera.Target, tileSize, maxDrawDistance);
            float halfW = worldW * 0.5f;
            float halfH = worldH * 0.5f;
            float nearestDx = MathF.Max(0f, MathF.Abs(dx) - halfW);
            float nearestDz = MathF.Max(0f, MathF.Abs(dz) - halfH);
            if (nearestDx * nearestDx + nearestDz * nearestDz > effectiveDraw * effectiveDraw)
                return false;

            var forward = camera.Target - camPos;
            float forwardLenSq = forward.X * forward.X + forward.Z * forward.Z;
            if (forwardLenSq < 1e-6f)
                return true;

            float invLen = 1f / MathF.Sqrt(forwardLenSq);
            float dot = (dx * forward.X + dz * forward.Z) * invLen;
            float behindPad = MathF.Max(worldW, worldH) * 0.35f;
            return dot > -behindPad;
        }
    }
}
