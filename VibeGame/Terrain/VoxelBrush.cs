using System;
using System.Numerics;
using Veilborne.Core.GameWorlds.Terrain;

namespace VibeGame.Terrain
{
    public static class VoxelBrush
    {
        // Returns weight in [0,1] based on falloff type
        public static float Falloff(float distance, float radius, VoxelFalloff falloff)
        {
            if (radius <= 0f) return 0f;
            float t = Math.Clamp(distance / radius, 0f, 1f);
            return falloff switch
            {
                VoxelFalloff.Linear => 1f - t,
                VoxelFalloff.Cosine => 0.5f * (1f + MathF.Cos(t * MathF.PI)),
                VoxelFalloff.Exponential => MathF.Exp(-4f * t * t),
                _ => 1f - t
            };
        }

        // Subtractive (dig) operation
        public static float ApplyDig(float density, float weight, float strength)
        {
            return density - weight * strength;
        }

        // Additive (fill) operation
        public static float ApplyFill(float density, float weight, float strength)
        {
            return density + weight * strength;
        }

        // Generic spherical brush operation
        private static void ApplySphere(VoxelChunk chunk, Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff, bool fill)
        {
            float voxel = chunk.VoxelSize;
            var origin = chunk.Origin;
            int n = chunk.Size;

            Vector3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new(float.MinValue, float.MinValue, float.MinValue);
            bool dirty = false;

            for (int z = 0; z < n; z++)
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float wx = origin.X + (x + 0.5f) * voxel;
                float wy = origin.Y + (y + 0.5f) * voxel;
                float wz = origin.Z + (z + 0.5f) * voxel;
                Vector3 p = new(wx, wy, wz);

                float d = Vector3.Distance(p, worldCenter);
                if (d > radius) continue;

                float w = Falloff(d, radius, falloff);
                float density = chunk.GetDensity(x, y, z);
                float newDensity = fill ? ApplyFill(density, w, strength) : ApplyDig(density, w, strength);

                chunk.SetDensity(x, y, z, newDensity);

                // Track dirty bounds
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
                dirty = true;
            }

            if (dirty)
                chunk.MarkDirtyRegion(min, max);
        }

        // Public: Dig a spherical region
        public static void DigSphereToChunk(VoxelChunk chunk, Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            ApplySphere(chunk, worldCenter, radius, strength, falloff, fill: false);
        }

        // Public: Fill a spherical region
        public static void FillSphereToChunk(VoxelChunk chunk, Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            ApplySphere(chunk, worldCenter, radius, strength, falloff, fill: true);
        }
    }
}
