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

        // Cached greedy mesh data for rendering
        public class MeshCache
        {
            public int LodLevel;
            public int TriangleCount;
            public DateTime GeneratedAt;
            internal VoxelGreedyMesher.MeshData Data = new VoxelGreedyMesher.MeshData();
        }

        public MeshCache? CachedMesh { get; private set; }
        public bool IsDirty { get; private set; }
        public int LodLevel { get; set; }

        private readonly object _meshLock = new object();
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

        // Enqueue async greedy mesh rebuild and cache results
        public void EnqueueRebuild()
        {
            if (!IsDirty) return;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _rebuildTask = Task.Run(() =>
            {
                try
                {
                    var data = VoxelGreedyMesher.Build(this, this.LodLevel);
                    var cache = new MeshCache
                    {
                        LodLevel = this.LodLevel,
                        TriangleCount = data.Triangles.Length / 3,
                        GeneratedAt = DateTime.UtcNow,
                        Data = data
                    };
                    lock (_meshLock)
                    {
                        CachedMesh = cache;
                        IsDirty = false;
                    }
                }
                catch (OperationCanceledException)
                {
                    // ignored
                }
            }, token);
        }

        public void RenderDebugBounds(Color color)
        {
            Vector3 size = new Vector3(Size * VoxelSize, Size * VoxelSize, Size * VoxelSize);
            Vector3 center = OriginWorld + size * 0.5f;
            Raylib.DrawCubeWires(center, size.X, size.Y, size.Z, color);
        }

        // Render greedy-meshed triangles with simple backface culling relative to camera
        public void RenderSurfaceCubes(Color color, Camera3D camera)
        {
            var cache = CachedMesh;
            if (cache == null || cache.TriangleCount == 0) return;
            var tris = cache.Data.Triangles;
            var norms = cache.Data.Normals;
            Vector3 cam = camera.position;
            for (int i = 0, t = 0; i < tris.Length; i += 3, t++)
            {
                Vector3 a = tris[i];
                Vector3 b = tris[i + 1];
                Vector3 c = tris[i + 2];
                Vector3 centroid = new Vector3((a.X + b.X + c.X) / 3f, (a.Y + b.Y + c.Y) / 3f, (a.Z + b.Z + c.Z) / 3f);
                Vector3 view = cam - centroid;
                float len2 = view.LengthSquared();
                if (len2 > 1e-6f) view /= MathF.Sqrt(len2);
                Vector3 n = norms[t];
                if (Vector3.Dot(n, view) <= 0f) continue; // backface culled
                Raylib.DrawTriangle3D(a, b, c, color);
            }
        }
    }
}
