using System;
using System.Numerics;

namespace VibeGame.Terrain
{
    public static class VoxelBrush
    {
        // Returns weight in [0,1] based on falloff type
        public static float Falloff(float distance, float radius, VoxelFalloff falloff)
        {
            if (radius <= 0f) return 0f;
            float t = Math.Clamp(distance / radius, 0f, 1f);
            switch (falloff)
            {
                case VoxelFalloff.Linear:
                    return 1f - t;
                case VoxelFalloff.Cosine:
                    // Smooth bell curve
                    return 0.5f * (1f + MathF.Cos(t * MathF.PI));
                case VoxelFalloff.Exponential:
                    // Faster falloff near edges
                    return MathF.Exp(-4.0f * t * t);
                default:
                    return 1f - t;
            }
        }

        // Apply subtractive (dig) operation to a density value
        // density < 0 => empty, density > 0 => solid (convention)
        public static float ApplyDig(float density, float weight, float strength)
        {
            // Move density toward empty by subtracting
            return density - weight * strength;
        }

        // Helper to apply a spherical brush to a voxel chunk bounds
        public static void ApplySphereToChunk(VoxelChunk chunk, Vector3 worldCenter, float radius, float strength, VoxelFalloff falloff)
        {
            // Iterate voxel centers and modify density
            float voxel = chunk.VoxelSize;
            var origin = chunk.OriginWorld;
            int n = chunk.Size;
            for (int z = 0; z < n; z++)
            {
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float wx = origin.X + (x + 0.5f) * voxel;
                        float wy = origin.Y + (y + 0.5f) * voxel;
                        float wz = origin.Z + (z + 0.5f) * voxel;
                        Vector3 p = new Vector3(wx, wy, wz);
                        float d = Vector3.Distance(p, worldCenter);
                        if (d > radius) continue;
                        float w = Falloff(d, radius, falloff);
                        float newD = ApplyDig(chunk.GetDensity(x, y, z), w, strength);
                        chunk.SetDensity(x, y, z, newD);
                    }
                }
            }
            chunk.MarkDirty();
        }
    }
}
