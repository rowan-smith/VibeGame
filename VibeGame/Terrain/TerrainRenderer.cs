using System;
using System.Numerics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using ZeroElectric.Vinculum;
using Serilog;
using VibeGame.Biomes;
using VibeGame.Core;

namespace VibeGame.Terrain
{
    public class TerrainRenderer : ITerrainRenderer
    {
        // Prepared mesh data built off-thread; uploaded on main thread
        private class MeshBuildJob
        {
            public Vector2 Origin;
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] UVs;
            public Color[] Colors;
            public ushort[] Indices;
        }

        private readonly ILogger logger = Log.ForContext<TerrainRenderer>();
        private readonly ITextureManager _textureManager;
        private readonly ITerrainTextureRegistry _terrainTextures;
        private readonly IBiomeProvider _biomeProvider;

        private Texture _lowTex;
        private bool _lowAvailable;
        private string? _lastBiomeIdApplied;

        private int _chunkSize = 16;
        private readonly Dictionary<Vector2, List<Chunk>> _chunksByOrigin = new();

        private readonly Dictionary<string, Material> _materialCache = new();
        private Material _activeMaterial;

        // Async mesh generation queue
        private readonly ConcurrentQueue<MeshBuildJob> _pendingUploads = new();
        private readonly object _queueLock = new();
        private readonly HashSet<Vector2> _pendingOrigins = new();

        public TerrainRenderer(ITextureManager textureManager, ITerrainTextureRegistry terrainTextures, IBiomeProvider biomeProvider)
        {
            _textureManager = textureManager;
            _terrainTextures = terrainTextures;
            _biomeProvider = biomeProvider;
        }

        public void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld)
        {
            // If we've already built and cached this origin's mesh, avoid re-uploading to the GPU
            if (_chunksByOrigin.TryGetValue(originWorld, out var existing) && existing is { Count: > 0 })
            {
                // Assume cached mesh is still valid for this origin; skip rebuild
                if (!existing[0].Mesh.Equals(default(Mesh)) && existing[0].Mesh.vertexCount > 0)
                    return;
            }

            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);

            // Build a single mesh that covers the entire provided heightmap (one VAO per chunk)
            int w = sizeX;
            int h = sizeZ;

            var chunk = new Chunk
            {
                Vertices = new Vector3[w * h],
                Normals = new Vector3[w * h],
                UVs = new Vector2[w * h],
                Colors = new Color[w * h],
                Indices = new ushort[(w - 1) * (h - 1) * 6]
            };

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int idx = x + z * w;
                float wx = originWorld.X + x * tileSize;
                float wz = originWorld.Y + z * tileSize;
                float y = heights[x, z];

                chunk.Vertices[idx] = new Vector3(wx, y, wz);
                chunk.Normals[idx] = ComputeNormal(heights, x, z, sizeX, sizeZ, tileSize);
                chunk.UVs[idx] = new Vector2(w > 1 ? (float)x / (w - 1) : 0f, h > 1 ? (float)z / (h - 1) : 0f);
                chunk.Colors[idx] = Raylib.WHITE;
            }

            int tri = 0;
            for (int z = 0; z < h - 1; z++)
            for (int x = 0; x < w - 1; x++)
            {
                int idx = x + z * w;
                chunk.Indices[tri++] = (ushort)idx;
                chunk.Indices[tri++] = (ushort)(idx + w);
                chunk.Indices[tri++] = (ushort)(idx + 1);

                chunk.Indices[tri++] = (ushort)(idx + 1);
                chunk.Indices[tri++] = (ushort)(idx + w);
                chunk.Indices[tri++] = (ushort)(idx + w + 1);
            }

            // Upload mesh using unsafe pointer (Vinculum / CsLo style)
            unsafe
            {
                fixed (Vector3* vPtr = chunk.Vertices)
                fixed (Vector2* uvPtr = chunk.UVs)
                fixed (Vector3* nPtr = chunk.Normals)
                fixed (Color* cPtr = chunk.Colors)
                fixed (ushort* iPtr = chunk.Indices)
                {
                    Mesh mesh = new Mesh
                    {
                        vertices = (float*)vPtr,
                        texcoords = (float*)uvPtr,
                        normals = (float*)nPtr,
                        colors = (byte*)cPtr,
                        indices = (ushort*)iPtr,
                        vertexCount = chunk.Vertices.Length,
                        triangleCount = chunk.Indices.Length / 3
                    };
                    // Use static (non-dynamic) buffers for mostly static terrain geometry
                    Raylib.UploadMesh(&mesh, false);
                    chunk.Mesh = mesh;
                }
            }

            _chunksByOrigin[originWorld] = new List<Chunk> { chunk };
        }

        public void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld)
        {
            // Skip if already built and cached
            if (_chunksByOrigin.TryGetValue(originWorld, out var existing) && existing is { Count: > 0 })
            {
                if (!existing[0].Mesh.Equals(default(Mesh)) && existing[0].Mesh.vertexCount > 0)
                    return;
            }

            lock (_queueLock)
            {
                if (_pendingOrigins.Contains(originWorld))
                    return;
                _pendingOrigins.Add(originWorld);
            }

            int w = heights.GetLength(0);
            int h = heights.GetLength(1);

            _ = Task.Run(() =>
            {
                var job = new MeshBuildJob
                {
                    Origin = originWorld,
                    Vertices = new Vector3[w * h],
                    Normals = new Vector3[w * h],
                    UVs = new Vector2[w * h],
                    Colors = new Color[w * h],
                    Indices = new ushort[(w - 1) * (h - 1) * 6]
                };

                for (int z = 0; z < h; z++)
                for (int x = 0; x < w; x++)
                {
                    int idx = x + z * w;
                    float wx = originWorld.X + x * tileSize;
                    float wz = originWorld.Y + z * tileSize;
                    float y = heights[x, z];

                    job.Vertices[idx] = new Vector3(wx, y, wz);
                    job.Normals[idx] = ComputeNormal(heights, x, z, w, h, tileSize);
                    job.UVs[idx] = new Vector2(w > 1 ? (float)x / (w - 1) : 0f, h > 1 ? (float)z / (h - 1) : 0f);
                    job.Colors[idx] = Raylib.WHITE;
                }

                int tri = 0;
                for (int z = 0; z < h - 1; z++)
                for (int x = 0; x < w - 1; x++)
                {
                    int idx = x + z * w;
                    job.Indices[tri++] = (ushort)idx;
                    job.Indices[tri++] = (ushort)(idx + w);
                    job.Indices[tri++] = (ushort)(idx + 1);

                    job.Indices[tri++] = (ushort)(idx + 1);
                    job.Indices[tri++] = (ushort)(idx + w);
                    job.Indices[tri++] = (ushort)(idx + w + 1);
                }

                _pendingUploads.Enqueue(job);
            });
        }

        public void ProcessBuildQueue(int maxPerFrame)
        {
            int processed = 0;
            while (processed < maxPerFrame && _pendingUploads.TryDequeue(out var job))
            {
                var chunk = new Chunk
                {
                    Vertices = job.Vertices,
                    Normals = job.Normals,
                    UVs = job.UVs,
                    Colors = job.Colors,
                    Indices = job.Indices
                };

                unsafe
                {
                    fixed (Vector3* vPtr = chunk.Vertices)
                    fixed (Vector2* uvPtr = chunk.UVs)
                    fixed (Vector3* nPtr = chunk.Normals)
                    fixed (Color* cPtr = chunk.Colors)
                    fixed (ushort* iPtr = chunk.Indices)
                    {
                        Mesh mesh = new Mesh
                        {
                            vertices = (float*)vPtr,
                            texcoords = (float*)uvPtr,
                            normals = (float*)nPtr,
                            colors = (byte*)cPtr,
                            indices = (ushort*)iPtr,
                            vertexCount = chunk.Vertices.Length,
                            triangleCount = chunk.Indices.Length / 3
                        };
                        Raylib.UploadMesh(&mesh, false);
                        chunk.Mesh = mesh;
                    }
                }

                _chunksByOrigin[job.Origin] = new List<Chunk> { chunk };
                lock (_queueLock)
                {
                    _pendingOrigins.Remove(job.Origin);
                }
                processed++;
            }
        }

        public void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor)
            => RenderAt(heights, tileSize, Vector2.Zero, camera);

        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera)
        {
            if (!_chunksByOrigin.TryGetValue(originWorld, out var list) || list == null) return;
            if (_activeMaterial.Equals(default(Material))) return;

            // Draw all loaded chunks for this origin. Distance-based culling is coordinated by ring radii.
            // Removing the hard clamp ensures far read-only/LOD rings are visible.
            foreach (var chunk in list)
            {
                Matrix4x4 transform = Matrix4x4.Identity;
                unsafe
                {
                    Raylib.DrawMesh(chunk.Mesh, _activeMaterial, transform);
                }
            }
        }

        public void PatchRegion(float[,] heights, float tileSize, Vector2 originWorld, int x0, int z0, int x1, int z1)
        {
            if (!_chunksByOrigin.TryGetValue(originWorld, out var list) || list == null || list.Count == 0)
                return;

            var chunk = list[0];
            int w = heights.GetLength(0);
            int h = heights.GetLength(1);

            // Clamp bounds to heightmap extents and expand by 1 to keep normals consistent at the border
            int sx0 = Math.Clamp(Math.Min(x0, x1) - 1, 0, w - 1);
            int sz0 = Math.Clamp(Math.Min(z0, z1) - 1, 0, h - 1);
            int sx1 = Math.Clamp(Math.Max(x0, x1) + 1, 0, w - 1);
            int sz1 = Math.Clamp(Math.Max(z0, z1) + 1, 0, h - 1);

            for (int z = sz0; z <= sz1; z++)
            for (int x = sx0; x <= sx1; x++)
            {
                int idx = x + z * w;
                float wx = originWorld.X + x * tileSize;
                float wz = originWorld.Y + z * tileSize;
                float y = heights[x, z];

                chunk.Vertices[idx] = new Vector3(wx, y, wz);
                chunk.Normals[idx] = ComputeNormal(heights, x, z, w, h, tileSize);
                // UVs/Colors unchanged
            }

            // Fallback: re-upload the full mesh. Some bindings might not expose partial buffer updates.
            unsafe
            {
                fixed (Vector3* vPtr = chunk.Vertices)
                fixed (Vector2* uvPtr = chunk.UVs)
                fixed (Vector3* nPtr = chunk.Normals)
                fixed (Color* cPtr = chunk.Colors)
                fixed (ushort* iPtr = chunk.Indices)
                {
                    var mesh = chunk.Mesh;
                    mesh.vertices = (float*)vPtr;
                    mesh.texcoords = (float*)uvPtr;
                    mesh.normals = (float*)nPtr;
                    mesh.colors = (byte*)cPtr;
                    mesh.indices = (ushort*)iPtr;
                    mesh.vertexCount = chunk.Vertices.Length;
                    mesh.triangleCount = chunk.Indices.Length / 3;
                    Raylib.UploadMesh(&mesh, false);
                    chunk.Mesh = mesh;
                }
            }
        }

        public void ApplyBiomeTextures(BiomeData biome)
        {
            if (biome == null) return;

            var biomeId = string.IsNullOrWhiteSpace(biome.Id) ? "default" : biome.Id;
            if (!_materialCache.TryGetValue(biomeId, out var mat) || mat.Equals(default(Material)))
            {
                mat = default;
                var layers = biome.SurfaceTextures ?? new List<SurfaceTextureLayer>();
                if (layers.Count > 0)
                {
                    var texId = layers[0].TextureId;

                    var albedo = _terrainTextures.GetResolvedAlbedoPath(texId);
                    var normal = _terrainTextures.GetResolvedNormalPath(texId);
                    var arm = _terrainTextures.GetResolvedArmPath(texId);
                    var aor = _terrainTextures.GetResolvedAorPath(texId);
                    var ao = _terrainTextures.GetResolvedAoPath(texId);
                    var rough = _terrainTextures.GetResolvedRoughPath(texId);
                    var metal = _terrainTextures.GetResolvedMetalPath(texId);
                    var disp = _terrainTextures.GetResolvedDisplacementPath(texId);

                    unsafe
                    {
                        var m = Raylib.LoadMaterialDefault();
                        Material* matPtr = &m;

                        // Always bind albedo if available
                        if (!string.IsNullOrWhiteSpace(albedo) && _textureManager.TryGetOrLoadByPath(albedo!, out var tAlb))
                        {
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ALBEDO, tAlb);
                        }

                        // Optional normal map
                        if (!string.IsNullOrWhiteSpace(normal) && _textureManager.TryGetOrLoadByPath(normal!, out var tNor))
                        {
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_NORMAL, tNor);
                        }

                        // AO/Rough/Metal combinations
                        if (!string.IsNullOrWhiteSpace(arm) && _textureManager.TryGetOrLoadByPath(arm!, out var tArm))
                        {
                            // Bind packed ARM texture to all three PBR slots; shader may select channels as needed
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_OCCLUSION, tArm); // R = AO
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ROUGHNESS, tArm); // G = Roughness
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tArm); // B = Metalness
                        }
                        else if (!string.IsNullOrWhiteSpace(aor) && _textureManager.TryGetOrLoadByPath(aor!, out var tAor))
                        {
                            // Bind AO/Rough packed to respective slots; metal may be separate or absent
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_OCCLUSION, tAor); // R = AO
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ROUGHNESS, tAor); // G = Roughness

                            if (!string.IsNullOrWhiteSpace(metal) && _textureManager.TryGetOrLoadByPath(metal!, out var tMet))
                            {
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tMet);
                            }
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(ao) && _textureManager.TryGetOrLoadByPath(ao!, out var tAo))
                            {
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_OCCLUSION, tAo);
                            }
                            if (!string.IsNullOrWhiteSpace(rough) && _textureManager.TryGetOrLoadByPath(rough!, out var tRgh))
                            {
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ROUGHNESS, tRgh);
                            }
                            if (!string.IsNullOrWhiteSpace(metal) && _textureManager.TryGetOrLoadByPath(metal!, out var tMet2))
                            {
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tMet2);
                            }
                        }

                        // Optional displacement/height map
                        if (!string.IsNullOrWhiteSpace(disp) && _textureManager.TryGetOrLoadByPath(disp!, out var tDisp))
                        {
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_HEIGHT, tDisp);
                        }

                        mat = m;
                    }
                }
                _materialCache[biomeId] = mat;
            }

            _activeMaterial = mat;
        }

        public void SetColorTint(Color color) { /* optional */ }

        private void EnsureMaterials()
        {
            // No-op: materials are prepared per-biome in ApplyBiomeTextures
        }

        private Vector3 ComputeNormal(float[,] heights, int x, int z, int sizeX, int sizeZ, float tileSize)
        {
            float hl = x > 0 ? heights[x - 1, z] : heights[x, z];
            float hr = x < sizeX - 1 ? heights[x + 1, z] : heights[x, z];
            float hd = z > 0 ? heights[x, z - 1] : heights[x, z];
            float hu = z < sizeZ - 1 ? heights[x, z + 1] : heights[x, z];

            Vector3 n = new Vector3(hl - hr, 2f * tileSize, hd - hu);
            return Vector3.Normalize(n);
        }

        public static Color ToRaylibColor(System.Drawing.Color c) => new Color(c.R, c.G, c.B, c.A);

        #region Helpers
        private class Chunk
        {
            public Vector3[] Vertices;
            public Vector3[] Normals;
            public Vector2[] UVs;
            public Color[] Colors;
            public ushort[] Indices;
            public Mesh Mesh;
        }
        #endregion

        // Upload an indexed mesh generated by VoxelGreedyMesher using static GPU buffers
        internal unsafe Mesh CreateMeshFromIndexed(VoxelGreedyMesher.MeshData data)
        {
            if (data is null || data.Vertices.Length == 0 || data.Indices.Length == 0)
                return default;

            // Raylib/Vinculum uses 16-bit indices; convert and clamp
            int triCount = data.Indices.Length / 3;
            var idx16 = new ushort[triCount * 3];
            for (int i = 0; i < idx16.Length; i++)
            {
                int v = data.Indices[i];
                if ((uint)v > ushort.MaxValue)
                    v = ushort.MaxValue; // clamp; typical chunks stay under 65k vertices
                idx16[i] = (ushort)v;
            }

            fixed (Vector3* vPtr = data.Vertices)
            fixed (Vector3* nPtr = data.Normals)
            fixed (ushort* iPtr = idx16)
            {
                Mesh mesh = new Mesh
                {
                    vertices = (float*)vPtr,
                    normals = (float*)nPtr,
                    indices = (ushort*)iPtr,
                    vertexCount = data.Vertices.Length,
                    triangleCount = triCount
                };
                Raylib.UploadMesh(&mesh, false); // static buffers
                return mesh;
            }
        }
    }
}
