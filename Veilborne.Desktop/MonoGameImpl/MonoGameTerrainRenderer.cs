using System.Numerics;
using Microsoft.Xna.Framework.Graphics;
using Serilog;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Veilborne.Biomes;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Settings;
using Veilborne.Sky;

namespace Veilborne.MonoGameImpl
{
    using XnaColor = Microsoft.Xna.Framework.Color;
    using XnaBoundingFrustum = Microsoft.Xna.Framework.BoundingFrustum;
    using XnaBoundingBox = Microsoft.Xna.Framework.BoundingBox;
    using XnaMatrix = Microsoft.Xna.Framework.Matrix;
    using XnaVector3 = Microsoft.Xna.Framework.Vector3;
    using XnaVector2 = Microsoft.Xna.Framework.Vector2;
    using XnaMathHelper = Microsoft.Xna.Framework.MathHelper;

    public class MonoGameTerrainRenderer : ITerrainRenderer
    {
        private readonly GraphicsDevice _graphicsDevice;
        private readonly BasicEffect _basicEffect;
        private readonly RasterizerState _wireframeRasterizer;
        private readonly RasterizerState _rasterizerNear;
        private readonly RasterizerState _rasterizerMid;
        private readonly RasterizerState _rasterizerFar;
        private readonly Dictionary<string, Texture2D?> _textureCache = new();
        private readonly ILogger _log = Log.ForContext<MonoGameTerrainRenderer>();
        private readonly IBiomeProvider? _biomeProvider;
        private readonly IGameSettingsService _settings;
        private readonly ISkyLightingService _sky;
        private readonly IShadowMapService _shadowMap;
        private readonly float _maxTerrainDrawDistance;
        private readonly float _secondaryPassDistanceScale;
        private Texture2D? _fallbackTerrainTexture;
        private readonly bool _biomeBlendShaderAvailable;

        private const int MaxTexSize = 512; // downscale 4K textures to avoid hitching
        private static readonly string[] FallbackTextureIds = { "brown_mud_leaves", "aerial_rocks", "lichen_rock", "snow" };

        private struct ChunkData
        {
            public enum LayerBlendMode
            {
                None = 0,
                SurfaceToSubsurface = 1,
                SubsurfaceToDeep = 2
            }

            public VertexBuffer Vb;
            public IndexBuffer Ib;
            public int IndexCount;
            public Texture2D? PrimaryTexture;
            public Texture2D? SecondaryTexture;
            public XnaColor Tint;
            public string BiomeId;   // dominant biome — rebuild key
            public string SecondaryBiomeId;
            public float SecondaryBlend;
            public float PrimaryLightingModifier;
            public float SecondaryLightingModifier;
            public float OriginX;
            public float OriginZ;
            public float WorldSize;
            public float MinY;
            public float MaxY;
            public float[,]? CurrentHeights;
            public float[,]? BaseHeights;
            public TerrainLayerConfig? LayerConfig;
            public Vector4[,]? Splatmap;
            public bool UseSplatLayering;
            public LayerBlendMode LayerMode;
            public float LayerBlendCoverage;
        }
        private readonly Dictionary<(float x, float z, float tile), ChunkData> _chunks = new();
        private readonly HashSet<(float x, float z, float tile)> _activeChunkKeys = new();
        private readonly List<(float x, float z, float tile)> _sortedActiveKeysScratch = new();
        private readonly Queue<(float x, float z, float tile)> _buildQueue = new();
        private readonly Dictionary<(float x, float z, float tile), (float[,] heights, float tileSize, System.Numerics.Vector2 origin)> _pendingBuildData = new();
        private readonly HashSet<(float x, float z, float tile)> _queuedBuildKeys = new();
        private readonly HashSet<(float x, float z, float tile)> _dirtyKeys = new();
        private readonly Dictionary<(int width, int depth), short[]> _cachedIndices16 = new();
        private readonly Dictionary<(int width, int depth), int[]> _cachedIndices32 = new();
        private VertexPositionColorTexture[]? _vertexScratch; // reusable vertex array to reduce GC pressure
        private BiomeData? _activeBiome;
        private BiomeData? _activeSecondaryBiome;
        private float _activeSecondaryBlend;
        private CameraComponent _pendingCamera;  // Deferred draw — render once per frame after all RenderAt calls
        private bool _hasPendingCamera;
        private int _syncBuildBudget; // cap synchronous BuildChunkMesh calls per frame

        // Per-frame rendering metrics (reset each Flush)
        private int _lastChunksDrawn;
        private int _lastDrawCalls;
        private int _lastSecondaryPasses;
        private int _lastMeshBuilds;
        private int _frameMeshBuilds;
        private int _lastEvicted;

        // Chunk eviction — cap GPU memory from stale chunks
        private const int MaxCachedChunks = 350;
        private const int EvictionBatchSize = 50;
        private const int MaxSyncBuildsPerFrame = 4;

        public int LastChunksDrawn => _lastChunksDrawn;
        public int LastDrawCalls => _lastDrawCalls;
        public int LastSecondaryPasses => _lastSecondaryPasses;
        public int LastMeshBuilds => _lastMeshBuilds;
        public int LastEvicted => _lastEvicted;
        public int TotalCachedChunks => _chunks.Count;

        public MonoGameTerrainRenderer(GraphicsDevice graphicsDevice, IGameSettingsService settings, ISkyLightingService sky, IShadowMapService shadowMap, IBiomeProvider? biomeProvider = null, IWorldConfigService? worldConfig = null)
        {
            _graphicsDevice = graphicsDevice;
            _settings = settings;
            _sky = sky;
            _shadowMap = shadowMap;
            _biomeProvider = biomeProvider;
            var terrainRuntime = worldConfig?.Config.TerrainRuntime;
            _maxTerrainDrawDistance = terrainRuntime?.MaxTerrainDrawDistance ?? 1300f;
            _secondaryPassDistanceScale = Math.Clamp(terrainRuntime?.SecondaryPassDistanceScale ?? 0.40f, 0.1f, 1.0f);
            _basicEffect = new BasicEffect(graphicsDevice) { LightingEnabled = false };
            _wireframeRasterizer = new RasterizerState { FillMode = FillMode.WireFrame, CullMode = CullMode.CullCounterClockwiseFace };
            _rasterizerNear = RasterizerState.CullCounterClockwise;
            _rasterizerMid = new RasterizerState
            {
                CullMode = CullMode.CullCounterClockwiseFace,
                DepthBias = 1,
                SlopeScaleDepthBias = 1f
            };
            _rasterizerFar = new RasterizerState
            {
                CullMode = CullMode.CullCounterClockwiseFace,
                DepthBias = 2,
                SlopeScaleDepthBias = 2f
            };
            _biomeBlendShaderAvailable = DetectBiomeBlendShaderAssets();
        }

        public void ApplyBiomeTextures(BiomeData biome)
        {
            _activeBiome = biome;
            _activeSecondaryBiome = null;
            _activeSecondaryBlend = 0f;
        }

        public void ApplyBiomeBlendTextures(BiomeData primary, BiomeData? secondary, float secondaryBlend)
        {
            _activeBiome = primary;
            _activeSecondaryBiome = secondary;
            _activeSecondaryBlend = Math.Clamp(secondaryBlend, 0f, 1f);
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
            if (_hasPendingCamera)
            {
                DrawChunks(_pendingCamera);
                _hasPendingCamera = false;
            }
            _lastMeshBuilds = _frameMeshBuilds;
            _frameMeshBuilds = 0;
            _syncBuildBudget = 0;

            // Evict stale chunks that are no longer in the active set
            _lastEvicted = 0;
            if (_chunks.Count > MaxCachedChunks)
            {
                int toEvict = Math.Min(_chunks.Count - MaxCachedChunks + EvictionBatchSize, _chunks.Count);
                var keysToRemove = new List<(float x, float z, float tile)>(toEvict);
                foreach (var key in _chunks.Keys)
                {
                    if (_activeChunkKeys.Contains(key)) continue;
                    keysToRemove.Add(key);
                    if (keysToRemove.Count >= toEvict) break;
                }
                foreach (var key in keysToRemove)
                {
                    if (_chunks.TryGetValue(key, out var stale))
                    {
                        stale.Vb?.Dispose();
                        stale.Ib?.Dispose();
                        _chunks.Remove(key);
                        _lastEvicted++;
                    }
                }
            }

            _activeChunkKeys.Clear();
        }

        public void RenderAt(float[,] heights, float tileSize, System.Numerics.Vector2 originWorld, CameraComponent camera)
        {
            var key = (originWorld.X, originWorld.Y, tileSize);
            _activeChunkKeys.Add(key);
            if (!_chunks.TryGetValue(key, out _) && _syncBuildBudget < MaxSyncBuildsPerFrame)
            {
                BuildChunkMesh(heights, tileSize, originWorld);
                _syncBuildBudget++;
            }
            if (_chunks.TryGetValue(key, out var existing))
            {
                existing.CurrentHeights = heights;
                bool allowBlendVisuals = _settings.Current.Graphics.BiomeTextureCrossfade;
                string primaryId = _activeBiome?.Id ?? string.Empty;
                string secondaryId = allowBlendVisuals ? (_activeSecondaryBiome?.Id ?? string.Empty) : string.Empty;
                float blend = allowBlendVisuals ? _activeSecondaryBlend : 0f;
                bool desiredUseSplat = existing.Splatmap != null && existing.LayerConfig != null && existing.BaseHeights != null;
                var desiredLayerMode = desiredUseSplat
                    ? ChunkData.LayerBlendMode.SurfaceToSubsurface
                    : ChunkData.LayerBlendMode.None;
                bool visualsMatch =
                    string.Equals(existing.BiomeId, primaryId, StringComparison.Ordinal) &&
                    string.Equals(existing.SecondaryBiomeId, secondaryId, StringComparison.Ordinal) &&
                    MathF.Abs(existing.SecondaryBlend - blend) < 0.001f &&
                    existing.LayerMode == desiredLayerMode &&
                    existing.UseSplatLayering == desiredUseSplat;
                if (!visualsMatch)
                    ApplyChunkVisual(ref existing, _activeBiome, _activeSecondaryBiome, blend, allowBlendVisuals);
                _chunks[key] = existing;
            }
            _pendingCamera = camera;  // Accumulate — actual draw happens via Flush()
            _hasPendingCamera = true;
        }

        public void RenderAt(float[,] heights, float tileSize, System.Numerics.Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig)
        {
            RenderAt(heights, tileSize, originWorld, camera, baseHeights, layerConfig, null);
        }

        public void RenderAt(float[,] heights, float tileSize, System.Numerics.Vector2 originWorld, CameraComponent camera, float[,]? baseHeights, TerrainLayerConfig? layerConfig, Vector4[,]? splatmap)
        {
            RenderAt(heights, tileSize, originWorld, camera);
            var key = (originWorld.X, originWorld.Y, tileSize);
            if (_chunks.TryGetValue(key, out var existing))
            {
                bool hadLayerData = existing.BaseHeights != null && existing.LayerConfig != null;
                bool hadSplatmap = existing.Splatmap != null;
                existing.BaseHeights = baseHeights;
                existing.LayerConfig = layerConfig;
                existing.Splatmap = splatmap;
                existing.UseSplatLayering = splatmap != null && baseHeights != null && layerConfig != null;
                existing.LayerMode = existing.UseSplatLayering
                    ? ChunkData.LayerBlendMode.SurfaceToSubsurface
                    : ChunkData.LayerBlendMode.None;
                _chunks[key] = existing;

                // First time we receive layer data for a chunk, rebuild once so per-vertex layer blend is populated.
                if ((!hadLayerData && baseHeights != null && layerConfig != null) || (!hadSplatmap && splatmap != null))
                {
                    BuildChunkMesh(heights, tileSize, originWorld);
                    if (_chunks.TryGetValue(key, out var rebuilt))
                        existing = rebuilt;
                }

                bool allowBlendVisuals = _settings.Current.Graphics.BiomeTextureCrossfade;
                ApplyChunkVisual(ref existing, _activeBiome, _activeSecondaryBiome, allowBlendVisuals ? _activeSecondaryBlend : 0f, allowBlendVisuals);
                _chunks[key] = existing;
            }
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
            _frameMeshBuilds++;
            int width = heights.GetLength(0);
            int depth = heights.GetLength(1);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            var key = (origin.X, origin.Y, tileSize);
            _chunks.TryGetValue(key, out var prevChunk);
            var baseHeights = prevChunk.BaseHeights;
            var layerConfig = prevChunk.LayerConfig;
            var splatmap = prevChunk.Splatmap;
            bool useSplatLayering = splatmap != null && baseHeights != null && layerConfig != null;
            var layerMode = useSplatLayering
                ? ChunkData.LayerBlendMode.SurfaceToSubsurface
                : ChunkData.LayerBlendMode.None;

            // Per-vertex biome blend for smooth boundary transitions (replaces chunk-center sampling)
            // Sample biome blend at chunk corners only and bilinearly interpolate across vertices
            // to avoid calling GetBlendWeightsAt() per-vertex (was 1024 calls → now 4).
            bool computePerVertexBiomeBlend = _biomeProvider is not null && !useSplatLayering;
            float cornerAlpha00 = 0f, cornerAlpha10 = 0f, cornerAlpha01 = 0f, cornerAlpha11 = 0f;
            if (computePerVertexBiomeBlend)
            {
                var bw = new BiomeWeight[4];
                float chunkW = (width - 1) * tileSize;
                float chunkD = (depth - 1) * tileSize;

                _biomeProvider!.GetBlendWeightsAt(new System.Numerics.Vector2(origin.X, origin.Y), null!, bw, out int c0, 4);
                cornerAlpha00 = c0 > 1 ? 1f - bw[0].Weight : 0f;

                _biomeProvider.GetBlendWeightsAt(new System.Numerics.Vector2(origin.X + chunkW, origin.Y), null!, bw, out int c1, 4);
                cornerAlpha10 = c1 > 1 ? 1f - bw[0].Weight : 0f;

                _biomeProvider.GetBlendWeightsAt(new System.Numerics.Vector2(origin.X, origin.Y + chunkD), null!, bw, out int c2, 4);
                cornerAlpha01 = c2 > 1 ? 1f - bw[0].Weight : 0f;

                _biomeProvider.GetBlendWeightsAt(new System.Numerics.Vector2(origin.X + chunkW, origin.Y + chunkD), null!, bw, out int c3, 4);
                cornerAlpha11 = c3 > 1 ? 1f - bw[0].Weight : 0f;
            }

            // UV: repeat texture every 8 world-units so it tiles naturally
            const float texWorldRepeat = 8f;

            var vertexCount = width * depth;
            if (_vertexScratch == null || _vertexScratch.Length < vertexCount)
                _vertexScratch = new VertexPositionColorTexture[vertexCount];
            var vertices = _vertexScratch;
            int blendSamples = 0;
            int blendNonZero = 0;
            for (int z = 0; z < depth; z++)
            for (int x = 0; x < width; x++)
            {
                float y = heights[x, z];
                float worldX = origin.X + x * tileSize;
                float worldZ = origin.Y + z * tileSize;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                byte blendAlpha = 0;

                // Bilinear interpolation from corner biome blend samples
                if (computePerVertexBiomeBlend)
                {
                    float tx = (width > 1) ? x / (float)(width - 1) : 0f;
                    float tz = (depth > 1) ? z / (float)(depth - 1) : 0f;
                    float lerpZ0 = cornerAlpha00 + (cornerAlpha10 - cornerAlpha00) * tx;
                    float lerpZ1 = cornerAlpha01 + (cornerAlpha11 - cornerAlpha01) * tx;
                    float alpha = lerpZ0 + (lerpZ1 - lerpZ0) * tz;
                    blendAlpha = (byte)Math.Clamp((int)(alpha * 255f), 0, 255);
                }

                // Splatmap/depth layer overrides (for digging system)
                if (useSplatLayering && splatmap != null && x < splatmap.GetLength(0) && z < splatmap.GetLength(1))
                {
                    var sw = splatmap[x, z];
                    float alpha = ComputeLayerBlendAlphaFromSplat(sw, layerMode);
                    blendAlpha = (byte)Math.Clamp((int)(alpha * 255f), 0, 255);
                    blendSamples++;
                    if (alpha > 0.02f) blendNonZero++;
                }
                else if (baseHeights != null && layerConfig != null)
                {
                    float depthDelta = MathF.Max(0f, baseHeights[x, z] - y);
                    blendAlpha = (byte)Math.Clamp((int)(ComputeLayerBlendAlpha(depthDelta, layerConfig, layerMode) * 255f), 0, 255);
                }
                XnaColor vertexColor = new XnaColor((byte)255, (byte)255, (byte)255, blendAlpha);

                var uv = new XnaVector2(worldX / texWorldRepeat, worldZ / texWorldRepeat);
                vertices[z * width + x] = new VertexPositionColorTexture(
                    new XnaVector3(worldX, y, worldZ),
                    vertexColor, uv);
            }

            int indexCount = (width - 1) * (depth - 1) * 6;

            // Reuse existing vertex/index buffers when size matches to avoid GPU allocation churn
            VertexBuffer vb;
            IndexBuffer ib;
            bool reuseBuffers = _chunks.TryGetValue(key, out var old) &&
                                old.Vb != null && old.Ib != null &&
                                old.Vb.VertexCount == vertexCount;

            if (reuseBuffers)
            {
                vb = old!.Vb!;
                vb.SetData(vertices, 0, vertexCount);
                ib = old.Ib!;
                // Index topology is fixed for same-size chunks; no need to re-upload indices
            }
            else
            {
                if (_chunks.TryGetValue(key, out var prev))
                {
                    prev.Vb?.Dispose();
                    prev.Ib?.Dispose();
                }

                vb = new VertexBuffer(_graphicsDevice, typeof(VertexPositionColorTexture), vertexCount, BufferUsage.WriteOnly);
                vb.SetData(vertices, 0, vertexCount);

                if (vertexCount <= 65535)
                {
                    var indices = GetOrCreateIndices16(width, depth);
                    ib = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
                    ib.SetData(indices);
                }
                else
                {
                    var indices = GetOrCreateIndices32(width, depth);
                    ib = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits, indices.Length, BufferUsage.WriteOnly);
                    ib.SetData(indices);
                }
            }

            _chunks[key] = new ChunkData
            {
                Vb = vb,
                Ib = ib,
                IndexCount = indexCount,
                PrimaryTexture = null,
                SecondaryTexture = null,
                Tint = XnaColor.White,
                BiomeId = "",
                SecondaryBiomeId = "",
                SecondaryBlend = 0f,
                PrimaryLightingModifier = 1f,
                SecondaryLightingModifier = 1f,
                OriginX = origin.X,
                OriginZ = origin.Y,
                WorldSize = MathF.Max(1f, MathF.Max((width - 1) * tileSize, (depth - 1) * tileSize)),
                MinY = float.IsFinite(minY) ? minY : 0f,
                MaxY = float.IsFinite(maxY) ? maxY : 0f,
                CurrentHeights = heights,
                BaseHeights = baseHeights,
                LayerConfig = layerConfig,
                Splatmap = splatmap,
                UseSplatLayering = useSplatLayering,
                LayerMode = layerMode,
                LayerBlendCoverage = blendSamples > 0 ? blendNonZero / (float)blendSamples : 0f
            };
        }

        private short[] GetOrCreateIndices16(int width, int depth)
        {
            var key = (width, depth);
            if (_cachedIndices16.TryGetValue(key, out var cached))
                return cached;

            int[] source = BuildTriangleIndices(width, depth);
            var indices = new short[source.Length];
            for (int i = 0; i < source.Length; i++)
                indices[i] = (short)source[i];
            _cachedIndices16[key] = indices;
            return indices;
        }

        private int[] GetOrCreateIndices32(int width, int depth)
        {
            var key = (width, depth);
            if (_cachedIndices32.TryGetValue(key, out var cached))
                return cached;

            var indices = BuildTriangleIndices(width, depth);
            _cachedIndices32[key] = indices;
            return indices;
        }

        private static int[] BuildTriangleIndices(int width, int depth)
        {
            int[] indices = new int[(width - 1) * (depth - 1) * 6];
            int cursor = 0;
            for (int z = 0; z < depth - 1; z++)
            for (int x = 0; x < width - 1; x++)
            {
                int i0 = z * width + x;
                int i1 = i0 + 1;
                int i2 = (z + 1) * width + x;
                int i3 = i2 + 1;

                indices[cursor++] = i0;
                indices[cursor++] = i1;
                indices[cursor++] = i2;
                indices[cursor++] = i1;
                indices[cursor++] = i3;
                indices[cursor++] = i2;
            }

            return indices;
        }

        private void ApplyChunkVisual(ref ChunkData chunk, BiomeData? biome, BiomeData? secondaryBiome, float secondaryBlend, bool allowBlendVisuals)
        {
            if (!allowBlendVisuals)
            {
                secondaryBiome = null;
                secondaryBlend = 0f;
            }

            if (biome == null)
            {
                chunk.BiomeId = "";
                chunk.SecondaryBiomeId = "";
                chunk.SecondaryBlend = 0f;
                chunk.PrimaryLightingModifier = 1f;
                chunk.SecondaryLightingModifier = 1f;
                chunk.Tint = XnaColor.White;
                chunk.PrimaryTexture = GetFallbackTexture();
                chunk.SecondaryTexture = null;
                return;
            }

            chunk.BiomeId = biome.Id;
            chunk.SecondaryBiomeId = secondaryBiome?.Id ?? string.Empty;
            chunk.SecondaryBlend = secondaryBlend;
            // Keep terrain lighting stable across chunk/biome boundaries.
            chunk.PrimaryLightingModifier = 1f;
            chunk.SecondaryLightingModifier = 1f;
            chunk.Tint = ComputeStableTerrainTint(biome);

            // Layered terrain mode: use per-vertex alpha as splat weight between material pairs.
            if (chunk.BaseHeights != null && chunk.LayerConfig != null)
            {
                var lc = chunk.LayerConfig;
                chunk.LayerMode = ChunkData.LayerBlendMode.SurfaceToSubsurface;
                float rockCoverage = EstimateRockCoverage(chunk);
                string? layeredPrimaryId = rockCoverage > 0.28f && !string.IsNullOrWhiteSpace(lc.SlopeTextureId)
                    ? lc.SlopeTextureId
                    : lc.SurfaceTextureId;
                chunk.PrimaryTexture = LoadTexture(layeredPrimaryId) ?? GetFallbackTexture();
                chunk.SecondaryTexture = LoadTexture(lc.SubsurfaceTextureId);
                // Keep lighting consistent across chunk state transitions.
                chunk.PrimaryLightingModifier = 1f;
                chunk.SecondaryLightingModifier = 1f;
                return;
            }

            Texture2D? primary = null;
            var primaryTextureId = SelectTextureForChunk(biome, chunk);
            if (!string.IsNullOrWhiteSpace(primaryTextureId))
                primary = LoadTexture(primaryTextureId);
            chunk.PrimaryTexture = primary ?? GetFallbackTexture();

            Texture2D? secondary = null;
            if (allowBlendVisuals && secondaryBiome is not null && secondaryBlend > 0.03f)
            {
                var secondaryTextureId = SelectTextureForChunk(secondaryBiome, chunk);
                if (!string.IsNullOrWhiteSpace(secondaryTextureId))
                    secondary = LoadTexture(secondaryTextureId);
            }
            chunk.SecondaryTexture = secondary;
        }

        private static string? SelectTextureForChunk(BiomeData biome, ChunkData chunk)
        {
            if (biome.TextureRules is { Count: > 0 })
            {
                float altitude = chunk.MaxY;
                foreach (var kv in biome.TextureRules)
                {
                    var rule = kv.Value;
                    if (rule == null) continue;
                    if (rule.MinAltitude.HasValue && altitude < rule.MinAltitude.Value) continue;
                    if (rule.MaxAltitude.HasValue && altitude > rule.MaxAltitude.Value) continue;
                    return kv.Key;
                }
            }

            if (biome.SurfaceTextures?.Count > 0)
                return biome.SurfaceTextures[0].TextureId;
            return null;
        }

        private static (float avgDepth, float maxDepth) EstimateDigDepth(ChunkData chunk)
        {
            if (chunk.BaseHeights == null || chunk.CurrentHeights == null) return (0f, 0f);
            int w = Math.Min(chunk.BaseHeights.GetLength(0), chunk.CurrentHeights.GetLength(0));
            int h = Math.Min(chunk.BaseHeights.GetLength(1), chunk.CurrentHeights.GetLength(1));
            if (w == 0 || h == 0) return (0f, 0f);
            float sum = 0f;
            float maxDepth = 0f;
            int count = 0;
            int sx = Math.Max(1, w / 8);
            int sz = Math.Max(1, h / 8);
            for (int z = 0; z < h; z += sz)
            for (int x = 0; x < w; x += sx)
            {
                float baseH = chunk.BaseHeights[x, z];
                float curH = chunk.CurrentHeights[x, z];
                float depth = MathF.Max(0f, baseH - curH);
                sum += depth;
                if (depth > maxDepth) maxDepth = depth;
                count++;
            }
            return count > 0 ? (sum / count, maxDepth) : (0f, 0f);
        }

        private static ChunkData.LayerBlendMode DetermineLayerBlendMode(float[,]? baseHeights, float[,]? currentHeights, TerrainLayerConfig? config, Vector4[,]? splatmap)
        {
            if (splatmap != null)
            {
                int sw = splatmap.GetLength(0);
                int sh = splatmap.GetLength(1);
                if (sw > 0 && sh > 0)
                {
                    int stepX = Math.Max(1, sw / 8);
                    int stepZ = Math.Max(1, sh / 8);
                    float deep = 0f;
                    float sub = 0f;
                    int deepCoverage = 0;
                    int sampleCount = 0;
                    for (int z = 0; z < sh; z += stepZ)
                    for (int x = 0; x < sw; x += stepX)
                    {
                        var v = splatmap[x, z];
                        float deepSample = MathF.Max(0f, v.Z + v.W);
                        deep += deepSample;
                        sub += MathF.Max(0f, v.Y);
                        if (deepSample > 0.6f) deepCoverage++;
                        sampleCount++;
                    }
                    float deepCoverageRatio = sampleCount > 0 ? deepCoverage / (float)sampleCount : 0f;
                    if (sampleCount > 0 && (deepCoverageRatio > 0.30f || deep > sub * 0.95f))
                        return ChunkData.LayerBlendMode.SubsurfaceToDeep;
                    if (sampleCount > 0 && (deep > 0f || sub > 0f))
                        return ChunkData.LayerBlendMode.SurfaceToSubsurface;
                }
            }

            if (baseHeights == null || currentHeights == null || config == null)
                return ChunkData.LayerBlendMode.None;

            int w = Math.Min(baseHeights.GetLength(0), currentHeights.GetLength(0));
            int h = Math.Min(baseHeights.GetLength(1), currentHeights.GetLength(1));
            if (w == 0 || h == 0) return ChunkData.LayerBlendMode.None;

            int sx = Math.Max(1, w / 8);
            int sz = Math.Max(1, h / 8);
            float deepPresence = 0f;
            int count = 0;
            float denom = MathF.Max(0.05f, config.DeepDepth - config.SubsurfaceDepth);
            for (int z = 0; z < h; z += sz)
            for (int x = 0; x < w; x += sx)
            {
                float depth = MathF.Max(0f, baseHeights[x, z] - currentHeights[x, z]);
                float deep = Math.Clamp((depth - config.SubsurfaceDepth) / denom, 0f, 1f);
                deepPresence += deep;
                count++;
            }

            float avgDeep = count > 0 ? deepPresence / count : 0f;
            return avgDeep > 0.08f
                ? ChunkData.LayerBlendMode.SubsurfaceToDeep
                : ChunkData.LayerBlendMode.SurfaceToSubsurface;
        }

        private static float ComputeLayerBlendAlpha(float depth, TerrainLayerConfig config, ChunkData.LayerBlendMode mode)
        {
            if (mode == ChunkData.LayerBlendMode.SubsurfaceToDeep)
            {
                float denom = MathF.Max(0.05f, config.DeepDepth - config.SubsurfaceDepth);
                return Math.Clamp((depth - config.SubsurfaceDepth) / denom, 0f, 1f);
            }

            return Math.Clamp(depth / MathF.Max(0.05f, config.SubsurfaceDepth), 0f, 1f);
        }

        private RasterizerState SelectRasterizerForTileSize(float tileSize)
        {
            if (tileSize <= 1.5f) return _rasterizerNear;
            if (tileSize <= 3f) return _rasterizerMid;
            return _rasterizerFar;
        }

        private static bool TryGetVisibleChunkState(
            ChunkData chunk,
            XnaVector3 cameraPos,
            XnaBoundingFrustum frustum,
            float drawDistance,
            out float distanceSq)
        {
            float half = chunk.WorldSize * 0.5f;
            float yPad = 2f;
            var min = new XnaVector3(chunk.OriginX, chunk.MinY - yPad, chunk.OriginZ);
            var max = new XnaVector3(chunk.OriginX + chunk.WorldSize, chunk.MaxY + yPad, chunk.OriginZ + chunk.WorldSize);
            var bounds = new XnaBoundingBox(min, max);
            var center = new XnaVector3(chunk.OriginX + half, (chunk.MinY + chunk.MaxY) * 0.5f, chunk.OriginZ + half);

            var camDelta = center - cameraPos;
            distanceSq = camDelta.LengthSquared();
            float chunkDistanceLimit = drawDistance + chunk.WorldSize * 0.75f;
            if (distanceSq > chunkDistanceLimit * chunkDistanceLimit)
                return false;
            return frustum.Contains(bounds) != Microsoft.Xna.Framework.ContainmentType.Disjoint;
        }

        private static float ComputeLayerBlendAlphaFromSplat(Vector4 splat, ChunkData.LayerBlendMode mode)
        {
            if (mode == ChunkData.LayerBlendMode.SubsurfaceToDeep)
            {
                float deep = MathF.Max(0f, splat.Z + splat.W);
                float sub = MathF.Max(0f, splat.Y);
                float denom = deep + sub;
                return denom > 1e-5f ? Math.Clamp(deep / denom, 0f, 1f) : 0f;
            }

            // Surface-to-subsurface: only exposed dirt from digging (Y channel).
            // Slope rock (Z) must not trigger the semi-transparent subsurface overlay pass.
            return Math.Clamp(splat.Y, 0f, 1f);
        }

        private static float EstimateRockCoverage(ChunkData chunk)
        {
            if (chunk.Splatmap == null) return 0f;
            int sw = chunk.Splatmap.GetLength(0);
            int sh = chunk.Splatmap.GetLength(1);
            if (sw == 0 || sh == 0) return 0f;

            float sum = 0f;
            int count = 0;
            int stepX = Math.Max(1, sw / 8);
            int stepZ = Math.Max(1, sh / 8);
            for (int z = 0; z < sh; z += stepZ)
            for (int x = 0; x < sw; x += stepX)
            {
                var v = chunk.Splatmap[x, z];
                sum += MathF.Max(0f, v.Z + v.W);
                count++;
            }

            return count > 0 ? sum / count : 0f;
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

        private bool DetectBiomeBlendShaderAssets()
        {
            string shadersDir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "shaders");
            string frag = System.IO.Path.Combine(shadersDir, "biome_blend.frag");
            string vert = System.IO.Path.Combine(shadersDir, "biome_blend.vert");
            bool ok = System.IO.File.Exists(frag) && System.IO.File.Exists(vert);
            if (!ok)
                _log.Warning("Biome blend shader assets not found under {Dir}; using BasicEffect fallback.", shadersDir);
            return ok;
        }

        // ── Rendering ────────────────────────────────────────────────────────────

        private void DrawChunks(CameraComponent camera)
        {
            if (_chunks.Count == 0) return;

            int chunksDrawn = 0;
            int drawCalls = 0;
            int secondaryPasses = 0;
            const int MaxDrawCallsPerFrame = 70;

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
            float renderDistanceScale = graphics.TerrainViewDistance / 100f;
            float brightness = graphics.Brightness / 100f;
            float drawDistance = _maxTerrainDrawDistance * renderDistanceScale;
            float maxDistSq = drawDistance * drawDistance;
            bool crossfadeEnabled = graphics.BiomeTextureCrossfade;

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

            // Draw nearer chunks first so the per-frame draw-call budget covers visible terrain.
            _sortedActiveKeysScratch.Clear();
            _sortedActiveKeysScratch.AddRange(_activeChunkKeys);
            _sortedActiveKeysScratch.Sort((a, b) =>
            {
                if (!_chunks.TryGetValue(a, out var ca)) return 1;
                if (!_chunks.TryGetValue(b, out var cb)) return -1;
                float da = (new XnaVector3(ca.OriginX + ca.WorldSize * 0.5f, (ca.MinY + ca.MaxY) * 0.5f, ca.OriginZ + ca.WorldSize * 0.5f) - pos).LengthSquared();
                float db = (new XnaVector3(cb.OriginX + cb.WorldSize * 0.5f, (cb.MinY + cb.MaxY) * 0.5f, cb.OriginZ + cb.WorldSize * 0.5f) - pos).LengthSquared();
                return da.CompareTo(db);
            });

            float secondaryPassDistance = drawDistance * _secondaryPassDistanceScale;
            float layeredCutoffSq = secondaryPassDistance * secondaryPassDistance * 0.25f;
            float crossfadeCutoffSq = secondaryPassDistance * secondaryPassDistance;
            const int MaxSecondaryDrawCallsPerFrame = 20;

            // Phase 1: draw every visible chunk's primary pass first so distant terrain
            // is not dropped when nearby chunks consume the budget on secondary passes.
            foreach (var key in _sortedActiveKeysScratch)
            {
                if (drawCalls >= MaxDrawCallsPerFrame) break;
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                if (!TryGetVisibleChunkState(chunk, pos, frustum, drawDistance, out _))
                    continue;

                _graphicsDevice.SetVertexBuffer(chunk.Vb);
                _graphicsDevice.Indices = chunk.Ib;
                if (!wireframe)
                    _graphicsDevice.RasterizerState = SelectRasterizerForTileSize(key.Item3);
                bool isLayeredChunk = chunk.UseSplatLayering &&
                                      chunk.LayerMode != ChunkData.LayerBlendMode.None;

                if (chunk.PrimaryTexture != null)
                {
                    _basicEffect.TextureEnabled = true;
                    // Primary pass is always opaque — blend alpha is only for the secondary pass.
                    _basicEffect.VertexColorEnabled = false;
                    _basicEffect.Texture = chunk.PrimaryTexture;
                    _basicEffect.Alpha = 1f;
                    var lit = _sky.AmbientColor + _sky.SunColor * _sky.SunIntensity * 0.50f;
                    float primaryLightMod = isLayeredChunk ? 1f : chunk.PrimaryLightingModifier;
                    _basicEffect.DiffuseColor = new XnaVector3(
                        Math.Clamp(lit.X * primaryLightMod, 0.20f, 1f) * brightness,
                        Math.Clamp(lit.Y * primaryLightMod, 0.20f, 1f) * brightness,
                        Math.Clamp(lit.Z * primaryLightMod, 0.20f, 1f) * brightness);
                }
                else
                {
                    _basicEffect.TextureEnabled = false;
                    _basicEffect.VertexColorEnabled = false;
                    _basicEffect.Alpha = 1f;
                    _basicEffect.DiffuseColor = new XnaVector3(0.7f * brightness, 0.7f * brightness, 0.72f * brightness);
                }
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, chunk.IndexCount / 3);
                    drawCalls++;
                }
                chunksDrawn++;
            }

            // Phase 2: optional subsurface / crossfade passes on a separate budget.
            foreach (var key in _sortedActiveKeysScratch)
            {
                if (secondaryPasses >= MaxSecondaryDrawCallsPerFrame) break;
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                if (!TryGetVisibleChunkState(chunk, pos, frustum, drawDistance, out float distanceSq))
                    continue;

                bool isLayeredChunk = chunk.UseSplatLayering &&
                                      chunk.LayerMode != ChunkData.LayerBlendMode.None;
                // Layered secondary is for dig exposure / biome crossfade only — not slope rock.
                bool hasVisibleLayerBlend = !isLayeredChunk || chunk.LayerBlendCoverage > 0.02f;
                bool shouldDrawSecondary = hasVisibleLayerBlend &&
                                            chunk.SecondaryTexture != null &&
                                            chunk.SecondaryTexture != chunk.PrimaryTexture &&
                                            ((isLayeredChunk && distanceSq <= layeredCutoffSq) ||
                                             (crossfadeEnabled && !isLayeredChunk && distanceSq <= crossfadeCutoffSq &&
                                              chunk.SecondaryBlend > 0.03f));
                if (!shouldDrawSecondary) continue;

                _graphicsDevice.SetVertexBuffer(chunk.Vb);
                _graphicsDevice.Indices = chunk.Ib;
                if (!wireframe)
                    _graphicsDevice.RasterizerState = SelectRasterizerForTileSize(key.Item3);

                var prevChunkBlend = _graphicsDevice.BlendState;
                _graphicsDevice.BlendState = isLayeredChunk ? BlendState.NonPremultiplied : BlendState.AlphaBlend;
                _basicEffect.TextureEnabled = true;
                _basicEffect.VertexColorEnabled = true;
                _basicEffect.Texture = chunk.SecondaryTexture;
                _basicEffect.Alpha = 1f;
                var lit = _sky.AmbientColor + _sky.SunColor * _sky.SunIntensity * 0.50f;
                float secondaryLightMod = isLayeredChunk ? 1f : chunk.SecondaryLightingModifier;
                _basicEffect.DiffuseColor = new XnaVector3(
                    Math.Clamp(lit.X * secondaryLightMod, 0.20f, 1f) * brightness,
                    Math.Clamp(lit.Y * secondaryLightMod, 0.20f, 1f) * brightness,
                    Math.Clamp(lit.Z * secondaryLightMod, 0.20f, 1f) * brightness);
                foreach (var pass in _basicEffect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, chunk.IndexCount / 3);
                    drawCalls++;
                }
                _graphicsDevice.BlendState = prevChunkBlend;
                secondaryPasses++;
            }

            _lastChunksDrawn = chunksDrawn;
            _lastDrawCalls = drawCalls;
            _lastSecondaryPasses = secondaryPasses;

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
            var imageFiles = System.IO.Directory
                .EnumerateFiles(dir)
                .Where(f =>
                {
                    var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
                    return ext is ".png" or ".jpg" or ".jpeg";
                })
                .ToArray();

            foreach (var f in imageFiles)
            {
                var name = System.IO.Path.GetFileName(f).ToLower();
                if (name.Contains("_diff_") || name.Contains("_albedo_") || name.Contains("_col_") || name.Contains("basecolor") || name.Contains("color"))
                { file = f; break; }
            }

            if (file == null)
            {
                file = imageFiles.FirstOrDefault(f =>
                {
                    var n = System.IO.Path.GetFileName(f).ToLowerInvariant();
                    return !(n.Contains("_nor_") || n.Contains("normal") || n.Contains("_rough_") || n.Contains("rough")
                        || n.Contains("_ao_") || n.Contains("ambientocclusion") || n.Contains("_metal_") || n.Contains("metallic")
                        || n.Contains("_disp_") || n.Contains("height"));
                }) ?? imageFiles.FirstOrDefault();
            }

            if (file == null)
            {
                _log.Warning("No terrain image found in terrain texture dir: {Dir}", dir);
                _textureCache[textureId] = null;
                return null;
            }

            try
            {
                _log.Debug("Loading terrain texture: {File}", System.IO.Path.GetFileName(file));
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
                _log.Debug("Terrain texture loaded: {Id} ({W}x{H})", textureId, tw, th);
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
