using Veilborne.Interfaces;
using Serilog;

namespace Veilborne.Core.MonoGameImpl
{
    using XnaColor = Microsoft.Xna.Framework.Color;
    using XnaBoundingFrustum = Microsoft.Xna.Framework.BoundingFrustum;
    using XnaBoundingSphere = Microsoft.Xna.Framework.BoundingSphere;
    using XnaMatrix = Microsoft.Xna.Framework.Matrix;
    using XnaVector3 = Microsoft.Xna.Framework.Vector3;
    using XnaVector2 = Microsoft.Xna.Framework.Vector2;
    using XnaMathHelper = Microsoft.Xna.Framework.MathHelper;
    using Microsoft.Xna.Framework.Graphics;
    using System.Numerics;
    using System.Collections.Generic;
using Veilborne.Biomes;
using Veilborne.Core.Settings;
using Veilborne.Core.Ecs.Components;
    using SixLabors.ImageSharp.PixelFormats;
    using SixLabors.ImageSharp.Processing;

    public class MonoGameTerrainRenderer : ITerrainRenderer
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly BasicEffect _basicEffect;
        private readonly RasterizerState _wireframeRasterizer;
        private readonly Dictionary<string, Texture2D?> _textureCache = new();
        private readonly ILogger _log = Log.ForContext<MonoGameTerrainRenderer>();
        private readonly IBiomeProvider? _biomeProvider;
        private readonly IGameSettingsService _settings;
        private Texture2D? _fallbackTerrainTexture;

        private const int MaxTexSize = 512; // downscale 4K textures to avoid hitching
        private const float MaxTerrainDrawDistance = 1100f;
        private static readonly string[] FallbackTextureIds = { "brown_mud_leaves", "aerial_rocks", "lichen_rock", "snow" };

        private struct ChunkData
        {
            public VertexBuffer Vb;
            public IndexBuffer Ib;
            public int IndexCount;
            public Texture2D? Texture;
            public XnaColor Tint;
            public string BiomeId;   // dominant biome — rebuild key
            public float OriginX;
            public float OriginZ;
            public float WorldSize;
            public float MinY;
            public float MaxY;
        }
        private readonly Dictionary<(float x, float z, float tile), ChunkData> _chunks = new();
        private readonly HashSet<(float x, float z, float tile)> _activeChunkKeys = new();
        private readonly Queue<(float x, float z, float tile)> _buildQueue = new();
        private readonly Dictionary<(float x, float z, float tile), (float[,] heights, float tileSize, System.Numerics.Vector2 origin)> _pendingBuildData = new();
        private readonly HashSet<(float x, float z, float tile)> _queuedBuildKeys = new();
        private readonly HashSet<(float x, float z, float tile)> _dirtyKeys = new();
        private BiomeData? _activeBiome;
        private CameraComponent? _pendingCamera;  // Deferred draw — render once per frame after all RenderAt calls

        public MonoGameTerrainRenderer(GraphicsDevice graphicsDevice, IGameSettingsService settings, IBiomeProvider? biomeProvider = null)
        {
            _graphicsDevice = graphicsDevice;
            _settings = settings;
            _biomeProvider = biomeProvider;
            _basicEffect = new BasicEffect(graphicsDevice) { LightingEnabled = false };
            _wireframeRasterizer = new RasterizerState { FillMode = FillMode.WireFrame, CullMode = CullMode.CullCounterClockwiseFace };
        }

        public void ApplyBiomeTextures(BiomeData biome)
        {
            _activeBiome = biome;
        }

        public void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend)
        {
            _activeBiome = primary;
        }

        public void SetColorTint(System.Numerics.Vector4 color)
        {
            // Applied per-chunk at draw time via the active biome
        }

        public void Render(float[,] heights, float tileSize, CameraComponent camera, System.Numerics.Vector3 baseColor)
        {
            RenderAt(heights, tileSize, System.Numerics.Vector2.Zero, camera);
        }

        /// <summary>Call once per frame after all RenderAt calls to issue the actual draw.</summary>
        public void Flush()
        {
            if (_pendingCamera != null)
            {
                DrawChunks(_pendingCamera);
                _pendingCamera = null;
            }
            _activeChunkKeys.Clear();
        }

        public void RenderAt(float[,] heights, float tileSize, System.Numerics.Vector2 originWorld, CameraComponent camera)
        {
            var key = (originWorld.X, originWorld.Y, tileSize);
            _activeChunkKeys.Add(key);
            if (!_chunks.TryGetValue(key, out _))
                BuildChunkMesh(heights, tileSize, originWorld);
            if (_chunks.TryGetValue(key, out var existing))
            {
                ApplyChunkVisual(ref existing, _activeBiome);
                _chunks[key] = existing;
            }
            _pendingCamera = camera;  // Accumulate — actual draw happens via Flush()
        }

        public void BuildChunks(float[,] heights, float tileSize, System.Numerics.Vector2 originWorld) =>
            BuildChunkMesh(heights, tileSize, originWorld);

        public void EnqueueBuild(float[,] heights, float tileSize, System.Numerics.Vector2 originWorld)
        {
            var key = (originWorld.X, originWorld.Y, tileSize);
            _pendingBuildData[key] = (heights, tileSize, originWorld);
            _dirtyKeys.Add(key);
            if (_queuedBuildKeys.Add(key))
                _buildQueue.Enqueue(key);
        }

        public void ProcessBuildQueue(int maxPerFrame)
        {
            int built = 0;
            while (built < maxPerFrame && _buildQueue.Count > 0)
            {
                var key = _buildQueue.Dequeue();
                _queuedBuildKeys.Remove(key);
                if (!_pendingBuildData.TryGetValue(key, out var pending))
                    continue;
                _pendingBuildData.Remove(key);
                var (h, ts, origin) = pending;
                BuildChunkMesh(h, ts, origin);
                _dirtyKeys.Remove(key);
                built++;
            }
        }

        public void MarkOriginDirty(System.Numerics.Vector2 originWorld)
        {
            foreach (var key in _chunks.Keys)
                if (Math.Abs(key.x - originWorld.X) < 1e-4f && Math.Abs(key.z - originWorld.Y) < 1e-4f)
                    _dirtyKeys.Add(key);
        }

        public void PatchRegion(float[,] heights, float tileSize, System.Numerics.Vector2 originWorld, int x0, int z0, int x1, int z1)
        {
            MarkOriginDirty(originWorld);
            EnqueueBuild(heights, tileSize, originWorld);
        }

        // ── Mesh building ────────────────────────────────────────────────────────

        private void BuildChunkMesh(float[,] heights, float tileSize, System.Numerics.Vector2 origin)
        {
            int width = heights.GetLength(0);
            int depth = heights.GetLength(1);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            // UV: repeat texture every 8 world-units so it tiles naturally
            const float texWorldRepeat = 8f;

            var vertices = new VertexPositionColorTexture[width * depth];
            for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                float y = heights[x, z];
                float worldX = origin.X + x * tileSize;
                float worldZ = origin.Y + z * tileSize;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                XnaColor vertexColor = XnaColor.White;

                var uv = new XnaVector2(worldX / texWorldRepeat, worldZ / texWorldRepeat);
                vertices[z * width + x] = new VertexPositionColorTexture(
                    new XnaVector3(worldX, y, worldZ),
                    vertexColor, uv);
            }

            var indices = new List<int>((width - 1) * (depth - 1) * 6);
            for (int z = 0; z < depth - 1; z++)
            for (int x = 0; x < width - 1; x++)
            {
                int i0 = z * width + x, i1 = z * width + x + 1;
                int i2 = (z + 1) * width + x, i3 = (z + 1) * width + x + 1;
                // CCW winding when viewed from above (+Y) so top face is front
                indices.Add(i0); indices.Add(i1); indices.Add(i2);
                indices.Add(i1); indices.Add(i3); indices.Add(i2);
            }

            var vb = new VertexBuffer(_graphicsDevice, typeof(VertexPositionColorTexture), vertices.Length, BufferUsage.WriteOnly);
            vb.SetData(vertices);

            IndexBuffer ib;
            if (vertices.Length <= 65535)
            {
                var si = indices.ConvertAll(i => (short)i).ToArray();
                ib = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits, si.Length, BufferUsage.WriteOnly);
                ib.SetData(si);
            }
            else
            {
                ib = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits, indices.Count, BufferUsage.WriteOnly);
                ib.SetData(indices.ToArray());
            }

            var key = (origin.X, origin.Y, tileSize);
            if (_chunks.TryGetValue(key, out var old))
            {
                old.Vb.Dispose();
                old.Ib.Dispose();
            }
            _chunks[key] = new ChunkData
            {
                Vb = vb,
                Ib = ib,
                IndexCount = indices.Count,
                Texture = null,
                Tint = XnaColor.White,
                BiomeId = "",
                OriginX = origin.X,
                OriginZ = origin.Y,
                WorldSize = MathF.Max(1f, MathF.Max((width - 1) * tileSize, (depth - 1) * tileSize)),
                MinY = float.IsFinite(minY) ? minY : 0f,
                MaxY = float.IsFinite(maxY) ? maxY : 0f
            };
        }

        private void ApplyChunkVisual(ref ChunkData chunk, BiomeData? biome)
        {
            if (biome == null)
            {
                chunk.BiomeId = "";
                chunk.Tint = XnaColor.White;
                chunk.Texture = GetFallbackTexture();
                return;
            }

            chunk.BiomeId = biome.Id;
            chunk.Tint = ComputeStableTerrainTint(biome);
            Texture2D? texture = null;
            if (biome.SurfaceTextures?.Count > 0)
                texture = LoadTexture(biome.SurfaceTextures[0].TextureId);
            chunk.Texture = texture ?? GetFallbackTexture();
        }

        private static XnaColor ComputeStableTerrainTint(BiomeData biome)
        {
            return XnaColor.White;
        }

        private Texture2D? GetFallbackTexture()
        {
            if (_fallbackTerrainTexture != null) return _fallbackTerrainTexture;
            foreach (var id in FallbackTextureIds)
            {
                var tex = LoadTexture(id);
                if (tex != null)
                {
                    _fallbackTerrainTexture = tex;
                    break;
                }
            }
            return _fallbackTerrainTexture;
        }

        // ── Rendering ────────────────────────────────────────────────────────────

        private void DrawChunks(CameraComponent camera)
        {
            if (_chunks.Count == 0) return;

            var pos = new XnaVector3(camera.Position.X, camera.Position.Y, camera.Position.Z);
            var target = new XnaVector3(camera.Target.X, camera.Target.Y, camera.Target.Z);
            var up = new XnaVector3(camera.Up.X, camera.Up.Y, camera.Up.Z);
            int w = _graphicsDevice.Viewport.Width, h = _graphicsDevice.Viewport.Height;
            float aspect = w > 0 && h > 0 ? (float)w / h : 16f / 9f;

            var view = XnaMatrix.CreateLookAt(pos, target, up);
            var proj = XnaMatrix.CreatePerspectiveFieldOfView(XnaMathHelper.ToRadians(camera.FovY), aspect, 0.1f, 5000f);
            _basicEffect.View = view;
            _basicEffect.Projection = proj;
            _basicEffect.World = XnaMatrix.Identity;
            _basicEffect.LightingEnabled = false;
            var frustum = new XnaBoundingFrustum(view * proj);
            var graphics = _settings.Current.Graphics;
            float renderDistanceScale = graphics.RenderDistance / 100f;
            float brightness = graphics.Brightness / 100f;
            float drawDistance = MaxTerrainDrawDistance * renderDistanceScale;
            float maxDistSq = drawDistance * drawDistance;

            var prevDepth = _graphicsDevice.DepthStencilState;
            var prevRaster = _graphicsDevice.RasterizerState;
            var prevSampler = _graphicsDevice.SamplerStates[0];
            var prevBlend = _graphicsDevice.BlendState;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.BlendState = BlendState.Opaque;
            bool wireframe = RuntimeEnvironment.IsDevelopmentEnvironment && _settings.Current.Debug.Wireframe;
            _graphicsDevice.RasterizerState = wireframe
                ? _wireframeRasterizer
                : RasterizerState.CullCounterClockwise;
            _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;  // tile textures

            foreach (var key in _activeChunkKeys)
            {
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                float half = chunk.WorldSize * 0.5f;
                var center = new XnaVector3(chunk.OriginX + half, (chunk.MinY + chunk.MaxY) * 0.5f, chunk.OriginZ + half);
                float yExtent = MathF.Max(2f, (chunk.MaxY - chunk.MinY) * 0.5f);
                float radius = MathF.Sqrt(half * half + yExtent * yExtent + half * half);
                var sphere = new XnaBoundingSphere(center, radius);

                var camDelta = center - pos;
                if (camDelta.LengthSquared() > maxDistSq) continue;
                if (!frustum.Intersects(sphere)) continue;

                _graphicsDevice.SetVertexBuffer(chunk.Vb);
                _graphicsDevice.Indices = chunk.Ib;

                if (chunk.Texture != null)
                {
                    _basicEffect.TextureEnabled = true;
                    _basicEffect.VertexColorEnabled = true;
                    _basicEffect.Texture = chunk.Texture;
                    _basicEffect.Alpha = 1f;
                    _basicEffect.DiffuseColor = new XnaVector3(brightness);
                }
                else
                {
                    _basicEffect.TextureEnabled = false;
                    _basicEffect.VertexColorEnabled = true;
                    _basicEffect.Alpha = 1f;
                    _basicEffect.DiffuseColor = new XnaVector3(0.7f * brightness, 0.7f * brightness, 0.72f * brightness);
                }
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, chunk.IndexCount / 3);
                }

            }

            _graphicsDevice.DepthStencilState = prevDepth;
            _graphicsDevice.RasterizerState = prevRaster;
            _graphicsDevice.SamplerStates[0] = prevSampler;
            _graphicsDevice.BlendState = prevBlend;
        }

        // ── Texture loading ───────────────────────────────────────────────────────

        private Texture2D? LoadTexture(string textureId)
        {
            if (_textureCache.TryGetValue(textureId, out var cached)) return cached;

            string dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "textures", "terrain", textureId);
            if (!System.IO.Directory.Exists(dir))
            {
                _log.Warning("Terrain texture directory not found: {Dir}", dir);
                _textureCache[textureId] = null;
                return null;
            }

            // Find the diffuse/albedo file
            string? file = null;
            foreach (var f in System.IO.Directory.GetFiles(dir, "*.png"))
            {
                var name = System.IO.Path.GetFileName(f).ToLower();
                if (name.Contains("_diff_") || name.Contains("_albedo_") || name.Contains("_col_"))
                { file = f; break; }
            }

            if (file == null)
            {
                _log.Warning("No diffuse PNG found in terrain texture dir: {Dir}", dir);
                _textureCache[textureId] = null;
                return null;
            }

            try
            {
                _log.Information("Loading terrain texture: {File}", System.IO.Path.GetFileName(file));
                using var img = SixLabors.ImageSharp.Image.Load<Rgba32>(file);

                // Downscale large textures to avoid hitching and excess VRAM use
                if (img.Width > MaxTexSize || img.Height > MaxTexSize)
                    img.Mutate(ctx => ctx.Resize(MaxTexSize, MaxTexSize));

                int tw = img.Width, th = img.Height;
                var data = new byte[tw * th * 4];
                int idx = 0;
                img.ProcessPixelRows(accessor =>
                {
                    for (int row = 0; row < accessor.Height; row++)
                    {
                        var span = accessor.GetRowSpan(row);
                        for (int col = 0; col < span.Length; col++)
                        {
                            data[idx++] = span[col].R;
                            data[idx++] = span[col].G;
                            data[idx++] = span[col].B;
                            data[idx++] = span[col].A;
                        }
                    }
                });
                var tex = new Texture2D(_graphicsDevice, tw, th, false, SurfaceFormat.Color);
                tex.SetData(data);
                _log.Information("Terrain texture loaded: {Id} ({W}x{H})", textureId, tw, th);
                _textureCache[textureId] = tex;
                return tex;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to load terrain texture: {Id}", textureId);
                _textureCache[textureId] = null;
                return null;
            }
        }
    }
}
