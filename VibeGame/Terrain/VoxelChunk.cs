using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Raylib_CsLo;

namespace VibeGame.Terrain
{
    // Local editable voxel volume stored in chunk coordinates
    public class VoxelChunk
    {
        public readonly int Size;              // N voxels per axis (e.g., 32)
        public readonly float VoxelSize;       // world meters per voxel (match tile size)
        public readonly Vector3 OriginWorld;   // world-space origin (min corner)

        // Density convention: >0 solid, <0 empty; iso-surface at 0
        private readonly float[,,] _density;

        // Cached mesh metadata placeholder (no real mesh for now)
        public class MeshCache
        {
            public int LodLevel;
            public int TriangleCount;
            public DateTime GeneratedAt;
        }

        public MeshCache? CachedMesh { get; private set; }
        public bool IsDirty { get; private set; }
        public int LodLevel { get; set; }

        private Task? _rebuildTask;
        private CancellationTokenSource? _cts;

        public VoxelChunk(Vector3 originWorld, int size, float voxelSize)
        {
            OriginWorld = originWorld;
            Size = size;
            VoxelSize = voxelSize;
            _density = new float[size, size, size];
            // Initialize densities to heightmap surface: empty above, solid below 0 Y plane by default
            for (int z = 0; z < size; z++)
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Start as solid to allow digging
                _density[x, y, z] = 1.0f;
            }
            IsDirty = true;
            LodLevel = 0;
        }

        public float GetDensity(int x, int y, int z) => _density[x, y, z];
        public void SetDensity(int x, int y, int z, float d) { _density[x, y, z] = d; }
        public void MarkDirty() { IsDirty = true; }

        public bool IntersectsSphere(Vector3 center, float radius)
        {
            // AABB-sphere test
            Vector3 min = OriginWorld;
            Vector3 max = OriginWorld + new Vector3(Size * VoxelSize, Size * VoxelSize, Size * VoxelSize);

            float cx = Math.Max(min.X, Math.Min(center.X, max.X));
            float cy = Math.Max(min.Y, Math.Min(center.Y, max.Y));
            float cz = Math.Max(min.Z, Math.Min(center.Z, max.Z));
            float dx = center.X - cx;
            float dy = center.Y - cy;
            float dz = center.Z - cz;
            return (dx * dx + dy * dy + dz * dz) <= radius * radius;
        }

        // Simulate async mesh rebuild with caching
        public void EnqueueRebuild()
        {
            if (!IsDirty) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _rebuildTask = Task.Run(async () =>
            {
                try
                {
                    // Simulate background work
                    await Task.Delay(10, token);
                    int tris = EstimateTriangleCount();
                    CachedMesh = new MeshCache
                    {
                        LodLevel = this.LodLevel,
                        TriangleCount = tris,
                        GeneratedAt = DateTime.UtcNow
                    };
                    IsDirty = false;
                }
                catch (OperationCanceledException)
                {
                    // ignored
                }
            }, token);
        }

        private int EstimateTriangleCount()
        {
            // Very rough estimate: proportionally to number of boundary voxels
            int n = Size;
            int boundary = 0;
            for (int z = 1; z < n - 1; z++)
            for (int y = 1; y < n - 1; y++)
            for (int x = 1; x < n - 1; x++)
            {
                float d = _density[x, y, z];
                // If near surface (around 0), count approximate triangles
                if (d > -0.2f && d < 0.2f) boundary++;
            }
            return boundary * 2; // placeholder
        }

        public void RenderDebugBounds(Color color)
        {
            Vector3 size = new Vector3(Size * VoxelSize, Size * VoxelSize, Size * VoxelSize);
            Vector3 center = OriginWorld + size * 0.5f;
            Raylib.DrawCubeWires(center, size.X, size.Y, size.Z, color);
        }

        // Very simple surface visualization: draw cubes at density boundary voxels
        public void RenderSurfaceCubes(Color color)
        {
            int n = Size;
            float vs = VoxelSize;
            // LOD-aware stepping to reduce draw calls
            int step = LodLevel <= 0 ? 1 : (LodLevel == 1 ? 2 : 3);
            // Slight vertical bias to avoid z-fighting with the heightmap surface
            float yBias = vs * 0.03f;

            for (int z = 1; z < n - 1; z += step)
            {
                for (int y = 1; y < n - 1; y += step)
                {
                    for (int x = 1; x < n - 1; x += step)
                    {
                        float d = _density[x, y, z];
                        // Only draw near-surface solids
                        if (d <= 0f) continue;

                        bool boundary =
                            _density[x - 1, y, z] < 0f || _density[x + 1, y, z] < 0f ||
                            _density[x, y - 1, z] < 0f || _density[x, y + 1, z] < 0f ||
                            _density[x, y, z - 1] < 0f || _density[x, y, z + 1] < 0f;

                        if (!boundary) continue;

                        Vector3 center = new Vector3(
                            OriginWorld.X + (x + 0.5f) * vs,
                            OriginWorld.Y + (y + 0.5f) * vs + yBias,
                            OriginWorld.Z + (z + 0.5f) * vs);
                        Vector3 size = new Vector3(vs, vs, vs);
                        Raylib.DrawCubeV(center, size, color);
                    }
                }
            }
        }
    }
}
