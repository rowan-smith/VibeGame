using System.Numerics;
using Veilborne.Ecs.Components;

namespace Veilborne.Terrain
{
    /// <summary>
    /// Cheap visibility tests for terrain rings before calling into the GPU renderer.
    /// </summary>
    public static class TerrainRenderCull
    {
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
            float diagonal = MathF.Max(worldW, worldH);
            float limit = maxDrawDistance + diagonal * 0.75f;
            if (dx * dx + dz * dz > limit * limit)
                return false;

            var forward = camera.Target - camPos;
            float forwardLenSq = forward.X * forward.X + forward.Z * forward.Z;
            if (forwardLenSq < 1e-6f)
                return true;

            float invLen = 1f / MathF.Sqrt(forwardLenSq);
            float dot = (dx * forward.X + dz * forward.Z) * invLen;
            float behindPad = diagonal * 0.35f;
            return dot > -behindPad;
        }
    }
}
