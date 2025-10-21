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
        private readonly TerrainTextureStreamingManager _streaming;
        private readonly Dictionary<string, IBiome> _biomesById = new(StringComparer.OrdinalIgnoreCase);

        private Texture _lowTex;
        private bool _lowAvailable;
        private string? _lastBiomeIdApplied;

        private int _chunkSize = 16;
        private readonly Dictionary<Vector2, List<Chunk>> _chunksByOrigin = new();

        private readonly Dictionary<string, Material> _materialCache = new();
        // Streamed textures cache by base path to reuse across materials
        private readonly Dictionary<string, StreamedTexture> _streamedTextures = new(StringComparer.OrdinalIgnoreCase);
        private Material _activeMaterial;
        private Shader _blendShader;
        private bool _blendShaderLoaded;

        // Async mesh generation queue
        private readonly ConcurrentQueue<MeshBuildJob> _pendingUploads = new();
        private readonly object _queueLock = new();
        private readonly HashSet<Vector2> _pendingOrigins = new();
        private readonly HashSet<Vector2> _dirtyOrigins = new();

        public TerrainRenderer(ITextureManager textureManager, ITerrainTextureRegistry terrainTextures, IBiomeProvider biomeProvider, IEnumerable<IBiome> allBiomes, TerrainTextureStreamingManager streaming)
        {
            _textureManager = textureManager;
            _terrainTextures = terrainTextures;
            _biomeProvider = biomeProvider;
            _streaming = streaming;
            foreach (var b in allBiomes)
            {
                if (!_biomesById.ContainsKey(b.Id)) _biomesById[b.Id] = b;
            }
        }

        public void BuildChunks(float[,] heights, float tileSize, Vector2 originWorld)
        {
            // If we've already built and cached this origin's mesh and it's not marked dirty, avoid re-uploading
            if (_chunksByOrigin.TryGetValue(originWorld, out var existing) && existing is { Count: > 0 })
            {
                bool isDirty;
                lock (_queueLock) { isDirty = _dirtyOrigins.Contains(originWorld); }
                if (!isDirty && !existing[0].Mesh.Equals(default(Mesh)) && existing[0].Mesh.vertexCount > 0)
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

            // Determine primary biome for this chunk area
            int chunkSize = Math.Max(1, w - 1);
            float expand = chunkSize * tileSize * 3.0f;
            var (primary, _) = BiomeSampling.GetDominantAndSecondaryBiomeForArea(_biomeProvider, null, originWorld, chunkSize, tileSize, 13, 2f, expand);
            string primaryId = primary.Id;

            for (int z = 0; z < h; z++)
            for (int x = 0; x < w; x++)
            {
                int idx = x + z * w;
                float wx = originWorld.X + x * tileSize;
                float wz = originWorld.Y + z * tileSize;
                float y = heights[x, z];

                chunk.Vertices[idx] = new Vector3(wx, y, wz);
                chunk.Normals[idx] = ComputeNormal(heights, x, z, sizeX, sizeZ, tileSize);
                // Use world-space continuous UVs scaled so each chunk roughly spans 1 repeat
                                float uvScale = 1f / MathF.Max(0.001f, ((w - 1) * tileSize));
                                chunk.UVs[idx] = new Vector2(wx, wz) * uvScale;
                float wAlpha = ComputeBlendWeight(new Vector2(wx, wz), primaryId, tileSize);
                byte a = (byte)(Clamp01(wAlpha) * 255f);
                chunk.Colors[idx] = new Color((byte)255, (byte)255, (byte)255, a);
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
            lock (_queueLock)
            {
                _dirtyOrigins.Remove(originWorld);
                _pendingOrigins.Remove(originWorld);
            }
        }

        public void EnqueueBuild(float[,] heights, float tileSize, Vector2 originWorld)
        {
            // Skip if already built and cached, unless explicitly marked dirty
            if (_chunksByOrigin.TryGetValue(originWorld, out var existing) && existing is { Count: > 0 })
            {
                bool isDirty;
                lock (_queueLock) { isDirty = _dirtyOrigins.Contains(originWorld); }
                if (!isDirty && !existing[0].Mesh.Equals(default(Mesh)) && existing[0].Mesh.vertexCount > 0)
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
                // Determine primary biome for this chunk area
                int chunkSizeLocal = Math.Max(1, w - 1);
                float expand = chunkSizeLocal * tileSize * 3.0f;
                var (primary, _) = BiomeSampling.GetDominantAndSecondaryBiomeForArea(_biomeProvider, null, originWorld, chunkSizeLocal, tileSize, 13, 2f, expand);
                string primaryId = primary.Id;

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
                    // Use world-space continuous UVs scaled so each chunk roughly spans 1 repeat
                                        float uvScale = 1f / MathF.Max(0.001f, ((w - 1) * tileSize));
                                        job.UVs[idx] = new Vector2(wx, wz) * uvScale;
                    // Compute alpha based on proximity to non-primary biome
                    float wAlpha = ComputeBlendWeight(new Vector2(wx, wz), primaryId, tileSize);
                    byte a = (byte)(Clamp01(wAlpha) * 255f);
                    job.Colors[idx] = new Color((byte)255, (byte)255, (byte)255, a);
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
                    _dirtyOrigins.Remove(job.Origin);
                }
                processed++;
            }
        }

        public void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor)
            => RenderAt(heights, tileSize, Vector2.Zero, camera);

        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera)
        {
            if (!_chunksByOrigin.TryGetValue(originWorld, out var list) || list == null) return;

            // Decide material per origin using dominant and secondary biome
            int w = heights.GetLength(0);
            int chunkSize = Math.Max(1, w - 1);
            // Expand sampling by ~3 chunk widths and use higher sample count for stability across borders
            float expand = chunkSize * tileSize * 3.0f;
            var (primary, secondary) = BiomeSampling.GetDominantAndSecondaryBiomeForArea(_biomeProvider, null, originWorld, chunkSize, tileSize, 13, 2f, expand);

            // Compute distance from camera to chunk center for mip selection
            Vector3 center = new Vector3(originWorld.X + ((w - 1) * 0.5f) * tileSize, 0f, originWorld.Y + ((w - 1) * 0.5f) * tileSize);
            float dist = Vector3.Distance(camera.position, center);
            int mip = _streaming.GetTargetMip(editable: false, distance: dist);
            ApplyBlendMaterial(primary.Data, secondary?.Data, mip);

            if (_activeMaterial.Equals(default(Material))) return;

            foreach (var chunk in list)
            {
                Matrix4x4 transform = Matrix4x4.Identity;
                Raylib.DrawMesh(chunk.Mesh, _activeMaterial, transform);
            }
        }

        public void MarkOriginDirty(Vector2 originWorld)
        {
            lock (_queueLock)
            {
                _dirtyOrigins.Add(originWorld);
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
            // Fallback single-biome material support (kept for compatibility)
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

                        if (!string.IsNullOrWhiteSpace(albedo) && _textureManager.TryGetOrLoadByPath(albedo!, out var tAlb))
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ALBEDO, tAlb);
                        if (!string.IsNullOrWhiteSpace(normal) && _textureManager.TryGetOrLoadByPath(normal!, out var tNor))
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_NORMAL, tNor);
                        if (!string.IsNullOrWhiteSpace(arm) && _textureManager.TryGetOrLoadByPath(arm!, out var tArm))
                        {
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_OCCLUSION, tArm);
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ROUGHNESS, tArm);
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tArm);
                        }
                        else if (!string.IsNullOrWhiteSpace(aor) && _textureManager.TryGetOrLoadByPath(aor!, out var tAor))
                        {
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_OCCLUSION, tAor);
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ROUGHNESS, tAor);
                            if (!string.IsNullOrWhiteSpace(metal) && _textureManager.TryGetOrLoadByPath(metal!, out var tMet))
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tMet);
                        }
                        else
                        {
                            if (!string.IsNullOrWhiteSpace(ao) && _textureManager.TryGetOrLoadByPath(ao!, out var tAo))
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_OCCLUSION, tAo);
                            if (!string.IsNullOrWhiteSpace(rough) && _textureManager.TryGetOrLoadByPath(rough!, out var tRgh))
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ROUGHNESS, tRgh);
                            if (!string.IsNullOrWhiteSpace(metal) && _textureManager.TryGetOrLoadByPath(metal!, out var tMet2))
                                Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tMet2);
                        }
                        if (!string.IsNullOrWhiteSpace(disp) && _textureManager.TryGetOrLoadByPath(disp!, out var tDisp))
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_HEIGHT, tDisp);

                        mat = m;
                    }
                }
                _materialCache[biomeId] = mat;
            }

            _activeMaterial = mat;
        }

        private void ApplyBlendMaterial(BiomeData primary, BiomeData? secondary, int mip)
        {
            if (primary == null) return;
            // Use a distinct key for the blend variant even when there is no secondary
            string key = secondary != null ? $"{primary.Id}|{secondary.Id}" : $"{primary.Id}|blend";
            if (!_materialCache.TryGetValue(key, out var mat) || mat.Equals(default(Material)))
            {
                mat = default;
                var priLayers = primary.SurfaceTextures ?? new List<SurfaceTextureLayer>();
                var secLayers = secondary?.SurfaceTextures ?? new List<SurfaceTextureLayer>();
                string? priAlbedo = null;
                string? secAlbedo = null;
                if (priLayers.Count > 0)
                {
                    var texId = priLayers[0].TextureId;
                    priAlbedo = _terrainTextures.GetResolvedAlbedoPath(texId);
                }
                if (secLayers.Count > 0)
                {
                    var texId2 = secLayers[0].TextureId;
                    secAlbedo = _terrainTextures.GetResolvedAlbedoPath(texId2);
                }

                unsafe
                {
                    var m = Raylib.LoadMaterialDefault();
                    // Always use the blend shader so vertex alpha never darkens the surface
                    EnsureBlendShader();
                    if (!_blendShader.Equals(default(Shader)))
                    {
                        m.shader = _blendShader;
                    }

                    Material* matPtr = &m;
                    Texture tAlb = default;
                    Texture tSec = default;
                    if (!string.IsNullOrWhiteSpace(priAlbedo))
                    {
                        var stPri = GetStreamedTexture(priAlbedo!);
                        if (stPri.EnsureMip(mip, out tAlb))
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_ALBEDO, tAlb);
                    }
                    // If no secondary albedo, bind the primary texture as the secondary as well
                    if (!string.IsNullOrWhiteSpace(secAlbedo))
                    {
                        var stSec = GetStreamedTexture(secAlbedo!);
                        if (stSec.EnsureMip(mip, out tSec))
                            Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tSec);
                    }
                    else if (!tAlb.Equals(default(Texture)))
                    {
                        Raylib.SetMaterialTexture(matPtr, MaterialMapIndex.MATERIAL_MAP_METALNESS, tAlb);
                    }

                    mat = m;
                }
                _materialCache[key] = mat;
            }
            _activeMaterial = mat;
        }

        private void EnsureBlendShader()
        {
            if (_blendShaderLoaded) return;
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string vs = System.IO.Path.Combine(baseDir, "assets", "shaders", "biome_blend.vert");
                string fs = System.IO.Path.Combine(baseDir, "assets", "shaders", "biome_blend.frag");
                _blendShader = Raylib.LoadShader(vs, fs);
            }
            catch { /* ignore, will fall back */ }
            finally { _blendShaderLoaded = true; }
        }

        private StreamedTexture GetStreamedTexture(string basePath)
        {
            if (!_streamedTextures.TryGetValue(basePath, out var st))
            {
                st = new StreamedTexture(basePath, _streaming, _textureManager);
                _streamedTextures[basePath] = st;
            }
            return st;
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

        private static float Clamp01(float v) => MathF.Max(0f, MathF.Min(1f, v));

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / MathF.Max(1e-6f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private float ComputeBlendWeight(Vector2 worldPos, string primaryId, float tileSize)
        {
            // Sample many directions at two radii; weight is average fraction that are NOT the primary biome
            const int samples = 16; // directions around the circle
            float r1 = MathF.Max(tileSize * 3.0f, 0.001f);
            float r2 = r1 * 2.2f;
            int other1 = 0, other2 = 0;

            for (int i = 0; i < samples; i++)
            {
                float a = (MathF.PI * 2f) * (i / (float)samples);
                Vector2 dir = new Vector2(MathF.Cos(a), MathF.Sin(a));
                var p1 = worldPos + dir * r1;
                var b1 = _biomeProvider.GetBiomeAt(p1, null);
                if (!string.Equals(b1.Id, primaryId, StringComparison.OrdinalIgnoreCase)) other1++;

                var p2 = worldPos + dir * r2;
                var b2 = _biomeProvider.GetBiomeAt(p2, null);
                if (!string.Equals(b2.Id, primaryId, StringComparison.OrdinalIgnoreCase)) other2++;
            }

            float frac1 = other1 / (float)samples;
            float frac2 = other2 / (float)samples;
            float frac = (frac1 * 0.6f) + (frac2 * 0.4f);
            // Softer thresholds for a wider transition band
            return Smoothstep(0.15f, 0.70f, frac);
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
