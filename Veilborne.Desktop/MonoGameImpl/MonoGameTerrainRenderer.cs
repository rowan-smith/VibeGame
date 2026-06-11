using System.Numerics;
using System.Runtime.CompilerServices;
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
            public VertexBuffer Vb;
            public IndexBuffer Ib;
            public int IndexCount;
            public Texture2D? PrimaryTexture;
            public Texture2D? SecondaryTexture;
            public Texture2D? MergeTexture;
            public XnaColor Tint;
            public string BiomeId;   // dominant biome — rebuild key
            public string MergeBiomeId;
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
            public TerrainChunkLayerBlendMode LayerMode;
            public float LayerBlendCoverage;
            public float BiomeBlendCoverage;
            public float TileSize;
            public bool BiomeMergeEvaluated;
            public float CachedMaxMerge;
        }

        private const float BiomeMergeCornerThreshold = 0.015f;
        private readonly Dictionary<(float x, float z, float tile), ChunkData> _chunks = new();
        private readonly HashSet<(float x, float z, float tile)> _activeChunkKeys = new();
        private readonly List<VisibleTerrainChunk> _visibleChunksScratch = new(128);
        private readonly List<TerrainDrawItem> _drawItemsScratch = new(192);
        private readonly Dictionary<(float x, float z, float tile), Task<TerrainMeshCpuBuilder.CpuBuildResult>> _cpuBuildTasks = new();
        private readonly EffectPass _terrainEffectPass;
        private readonly Queue<(float x, float z, float tile)> _buildQueue = new();
        private readonly Dictionary<(float x, float z, float tile), (float[,] heights, float tileSize, System.Numerics.Vector2 origin)> _pendingBuildData = new();
        private readonly HashSet<(float x, float z, float tile)> _queuedBuildKeys = new();
        private readonly HashSet<(float x, float z, float tile)> _dirtyKeys = new();
        private readonly Dictionary<(int width, int depth), short[]> _cachedIndices16 = new();
        private readonly Dictionary<(int width, int depth), int[]> _cachedIndices32 = new();
        private BiomeData? _activeBiome;
        private BiomeData? _activeSecondaryBiome;
        private float _activeSecondaryBlend;
        private CameraComponent _pendingCamera;  // Deferred draw — render once per frame after all RenderAt calls
        private bool _hasPendingCamera;
        private int _syncBuildBudget; // cap synchronous BuildChunkMesh calls per frame
        private bool _fastMeshBuild; // loading warmup: corner blend instead of per-vertex merge

        // Per-frame rendering metrics (reset each Flush)
        private int _lastChunksDrawn;
        private int _lastDrawCalls;
        private int _lastSecondaryPasses;
        private int _lastMeshBuilds;
        private int _frameMeshBuilds;
        private int _lastEvicted;
        private int _lastEffectApplies;
        private int _lastTextureBatches;

        // Chunk eviction — cap GPU memory from stale chunks
        private const int MaxCachedChunks = 350;
        private const int EvictionBatchSize = 50;
        private const int MaxSyncBuildsPerFrame = 4;

        public int LastChunksDrawn => _lastChunksDrawn;
        public int LastDrawCalls => _lastDrawCalls;
        public int LastSecondaryPasses => _lastSecondaryPasses;
        public int LastMeshBuilds => _lastMeshBuilds;
        public int LastEvicted => _lastEvicted;
        public int LastEffectApplies => _lastEffectApplies;
        public int LastTextureBatches => _lastTextureBatches;
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
            _terrainEffectPass = _basicEffect.CurrentTechnique.Passes[0];
        }

        private readonly struct VisibleTerrainChunk
        {
            public readonly (float X, float Z, float Tile) Key;
            public readonly float DistanceSq;
            public readonly int RasterTier;

            public VisibleTerrainChunk((float x, float z, float tile) key, float distanceSq, int rasterTier)
            {
                Key = (key.x, key.z, key.tile);
                DistanceSq = distanceSq;
                RasterTier = rasterTier;
            }
        }

        private enum TerrainPassKind : byte
        {
            Primary = 0,
            Merge = 1,
            Layer = 2
        }

        private readonly struct TerrainDrawItem : IComparable<TerrainDrawItem>
        {
            public readonly VisibleTerrainChunk Visible;
            public readonly Texture2D? Texture;
            public readonly bool TextureEnabled;
            public readonly XnaVector3 DiffuseColor;
            public readonly TerrainPassKind Pass;

            public TerrainDrawItem(
                VisibleTerrainChunk visible,
                Texture2D? texture,
                bool textureEnabled,
                XnaVector3 diffuseColor,
                TerrainPassKind pass)
            {
                Visible = visible;
                Texture = texture;
                TextureEnabled = textureEnabled;
                DiffuseColor = diffuseColor;
                Pass = pass;
            }

            public int CompareTo(TerrainDrawItem other)
            {
                int c = Pass.CompareTo(other.Pass);
                if (c != 0) return c;
                c = ReferenceCompare(Texture, other.Texture);
                if (c != 0) return c;
                c = Visible.RasterTier.CompareTo(other.Visible.RasterTier);
                if (c != 0) return c;
                return PackDiffuse(DiffuseColor).CompareTo(PackDiffuse(other.DiffuseColor));
            }
        }

        private static int ReferenceCompare(object? a, object? b)
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a is null) return -1;
            if (b is null) return 1;
            return RuntimeHelpers.GetHashCode(a).CompareTo(RuntimeHelpers.GetHashCode(b));
        }

        private static uint PackDiffuse(XnaVector3 diffuse)
            => ((uint)Math.Clamp((int)(diffuse.X * 255f), 0, 255) << 16)
             | ((uint)Math.Clamp((int)(diffuse.Y * 255f), 0, 255) << 8)
             | (uint)Math.Clamp((int)(diffuse.Z * 255f), 0, 255);

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

        public void SetWarmupMode(bool enabled) => _fastMeshBuild = enabled;

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
                existing.TileSize = tileSize;
                if (!existing.BiomeMergeEvaluated && _settings.Current.Graphics.BiomeTextureCrossfade)
                    EnqueueBuild(heights, tileSize, originWorld);

                string primaryId = _activeBiome?.Id ?? string.Empty;
                string mergeId = _activeSecondaryBiome?.Id ?? string.Empty;
                bool desiredUseSplat = existing.Splatmap != null && existing.LayerConfig != null && existing.BaseHeights != null;
                var desiredLayerMode = desiredUseSplat
                    ? TerrainChunkLayerBlendMode.SurfaceToSubsurface
                    : TerrainChunkLayerBlendMode.None;
                bool visualsMatch =
                    string.Equals(existing.BiomeId, primaryId, StringComparison.Ordinal) &&
                    string.Equals(existing.MergeBiomeId, mergeId, StringComparison.Ordinal) &&
                    existing.LayerMode == desiredLayerMode &&
                    existing.UseSplatLayering == desiredUseSplat;
                if (!visualsMatch)
                    ApplyChunkVisual(ref existing, _activeBiome, _activeSecondaryBiome, _activeSecondaryBlend);
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
                    ? TerrainChunkLayerBlendMode.SurfaceToSubsurface
                    : TerrainChunkLayerBlendMode.None;
                existing.TileSize = tileSize;

                // Rebuild when layer/splat data first arrives or when biome merge weights were missing from mesh.
                if ((!hadLayerData && baseHeights != null && layerConfig != null) ||
                    (!hadSplatmap && splatmap != null) ||
                    (!existing.BiomeMergeEvaluated && _settings.Current.Graphics.BiomeTextureCrossfade))
                    EnqueueBuild(heights, tileSize, originWorld);

                string primaryId = _activeBiome?.Id ?? string.Empty;
                string mergeId = _activeSecondaryBiome?.Id ?? string.Empty;
                bool visualsMatch =
                    string.Equals(existing.BiomeId, primaryId, StringComparison.Ordinal) &&
                    string.Equals(existing.MergeBiomeId, mergeId, StringComparison.Ordinal) &&
                    existing.UseSplatLayering == (splatmap != null && baseHeights != null && layerConfig != null);
                if (!visualsMatch)
                    ApplyChunkVisual(ref existing, _activeBiome, _activeSecondaryBiome, _activeSecondaryBlend);
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
            {
                _buildQueue.Enqueue(key);
                ScheduleCpuMeshBuild(key, heights, tileSize, originWorld);
            }
        }

        private void ScheduleCpuMeshBuild(
            (float x, float z, float tile) key,
            float[,] heights,
            float tileSize,
            System.Numerics.Vector2 originWorld)
        {
            if (_cpuBuildTasks.ContainsKey(key))
                return;

            _chunks.TryGetValue(key, out var prevChunk);
            var layer = CaptureLayerSnapshot(prevChunk);
            bool crossfade = _settings.Current.Graphics.BiomeTextureCrossfade;
            bool fast = _fastMeshBuild;
            var provider = _biomeProvider;

            _cpuBuildTasks[key] = Task.Run(() => TerrainMeshCpuBuilder.Build(
                heights, tileSize, originWorld, layer, provider, crossfade, fast));
        }

        private static TerrainMeshCpuBuilder.LayerSnapshot CaptureLayerSnapshot(ChunkData chunk)
        {
            return new TerrainMeshCpuBuilder.LayerSnapshot(
                chunk.BaseHeights,
                chunk.Splatmap,
                chunk.LayerConfig,
                chunk.UseSplatLayering,
                (byte)chunk.LayerMode);
        }

        public void ProcessBuildQueue(int maxPerFrame)
        {
            int built = 0;
            int queueSize = _buildQueue.Count;
            int rotations = 0;
            while (built < maxPerFrame && _buildQueue.Count > 0 && rotations <= queueSize)
            {
                var key = _buildQueue.Dequeue();
                rotations++;
                if (!_pendingBuildData.TryGetValue(key, out var pending))
                {
                    _queuedBuildKeys.Remove(key);
                    _cpuBuildTasks.Remove(key);
                    continue;
                }

                if (_cpuBuildTasks.TryGetValue(key, out var task))
                {
                    if (!task.IsCompleted)
                    {
                        _buildQueue.Enqueue(key);
                        continue;
                    }

                    _cpuBuildTasks.Remove(key);
                    if (task.IsFaulted)
                    {
                        _log.Warning(task.Exception, "Async terrain mesh build failed for {Key}", key);
                        BuildChunkMesh(pending.heights, pending.tileSize, pending.origin);
                    }
                    else
                        UploadMeshFromCpuResult(task.Result);
                }
                else
                    BuildChunkMesh(pending.heights, pending.tileSize, pending.origin);

                _pendingBuildData.Remove(key);
                _queuedBuildKeys.Remove(key);
                _dirtyKeys.Remove(key);
                built++;
            }
        }

        public void MarkOriginDirty(System.Numerics.Vector2 originWorld)
        {
            foreach (var key in _chunks.Keys)
            {
                if (Math.Abs(key.x - originWorld.X) > 1e-4f || Math.Abs(key.z - originWorld.Y) > 1e-4f)
                    continue;
                _dirtyKeys.Add(key);
                if (_chunks.TryGetValue(key, out var chunk))
                {
                    chunk.BiomeMergeEvaluated = false;
                    _chunks[key] = chunk;
                }
            }
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
            _chunks.TryGetValue((origin.X, origin.Y, tileSize), out var prevChunk);
            var layer = CaptureLayerSnapshot(prevChunk);
            var cpu = TerrainMeshCpuBuilder.Build(
                heights,
                tileSize,
                origin,
                layer,
                _biomeProvider,
                _settings.Current.Graphics.BiomeTextureCrossfade,
                _fastMeshBuild);
            UploadMeshFromCpuResult(cpu);
        }

        private void UploadMeshFromCpuResult(TerrainMeshCpuBuilder.CpuBuildResult cpu)
        {
            var key = cpu.Key;
            int width = cpu.Width;
            int depth = cpu.Depth;
            int vertexCount = cpu.Vertices.Length;
            int indexCount = (width - 1) * (depth - 1) * 6;

            bool hasOld = _chunks.TryGetValue(key, out var old);
            VertexBuffer vb;
            IndexBuffer ib;
            bool reuseBuffers = hasOld && old.Vb != null && old.Ib != null && old.Vb.VertexCount == vertexCount;
            if (reuseBuffers)
            {
                vb = old.Vb;
                vb.SetData(cpu.Vertices, 0, vertexCount);
                ib = old.Ib;
            }
            else
            {
                if (hasOld)
                {
                    old.Vb?.Dispose();
                    old.Ib?.Dispose();
                }
                vb = new VertexBuffer(_graphicsDevice, typeof(VertexPositionColorTexture), vertexCount, BufferUsage.WriteOnly);
                vb.SetData(cpu.Vertices, 0, vertexCount);
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

            var layer = cpu.Layer;
            _chunks[key] = new ChunkData
            {
                Vb = vb,
                Ib = ib,
                IndexCount = indexCount,
                PrimaryTexture = hasOld ? old.PrimaryTexture : null,
                SecondaryTexture = hasOld ? old.SecondaryTexture : null,
                MergeTexture = hasOld ? old.MergeTexture : null,
                Tint = hasOld ? old.Tint : XnaColor.White,
                BiomeId = hasOld ? old.BiomeId : string.Empty,
                MergeBiomeId = string.IsNullOrEmpty(cpu.MergeBiomeId)
                    ? (hasOld ? old.MergeBiomeId : string.Empty)
                    : cpu.MergeBiomeId,
                SecondaryBiomeId = hasOld ? old.SecondaryBiomeId : string.Empty,
                SecondaryBlend = hasOld ? old.SecondaryBlend : 0f,
                PrimaryLightingModifier = hasOld ? old.PrimaryLightingModifier : 1f,
                SecondaryLightingModifier = hasOld ? old.SecondaryLightingModifier : 1f,
                OriginX = cpu.Origin.X,
                OriginZ = cpu.Origin.Y,
                WorldSize = MathF.Max(1f, MathF.Max((width - 1) * cpu.TileSize, (depth - 1) * cpu.TileSize)),
                MinY = cpu.MinY,
                MaxY = cpu.MaxY,
                CurrentHeights = cpu.Heights,
                BaseHeights = layer.BaseHeights,
                LayerConfig = layer.LayerConfig,
                Splatmap = layer.Splatmap,
                UseSplatLayering = layer.UseSplatLayering,
                LayerMode = (TerrainChunkLayerBlendMode)layer.LayerMode,
                LayerBlendCoverage = cpu.LayerBlendCoverage,
                BiomeBlendCoverage = cpu.BiomeBlendCoverage,
                TileSize = cpu.TileSize,
                BiomeMergeEvaluated = true,
                CachedMaxMerge = cpu.CachedMaxMerge
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

        private void ApplyChunkVisual(ref ChunkData chunk, BiomeData? biome, BiomeData? mergeBiome, float maxMerge)
        {
            if (biome == null)
            {
                chunk.BiomeId = "";
                chunk.MergeBiomeId = "";
                chunk.SecondaryBiomeId = "";
                chunk.SecondaryBlend = 0f;
                chunk.PrimaryLightingModifier = 1f;
                chunk.SecondaryLightingModifier = 1f;
                chunk.Tint = XnaColor.White;
                chunk.PrimaryTexture = GetFallbackTexture();
                chunk.SecondaryTexture = null;
                chunk.MergeTexture = null;
                return;
            }

            chunk.BiomeId = biome.Id;
            chunk.MergeBiomeId = mergeBiome?.Id ?? string.Empty;
            chunk.SecondaryBiomeId = string.Empty;
            chunk.SecondaryBlend = 0f;
            chunk.PrimaryLightingModifier = 1f;
            chunk.SecondaryLightingModifier = 1f;
            chunk.Tint = ComputeStableTerrainTint(biome);

            bool useBiomeMerge = _settings.Current.Graphics.BiomeTextureCrossfade &&
                                 mergeBiome is not null &&
                                 (maxMerge > BiomeMergeCornerThreshold ||
                                  chunk.BiomeBlendCoverage > 0.02f ||
                                  chunk.CachedMaxMerge > BiomeMergeCornerThreshold);

            if (chunk.BaseHeights != null && chunk.LayerConfig != null)
            {
                var lc = chunk.LayerConfig;
                chunk.LayerMode = TerrainChunkLayerBlendMode.SurfaceToSubsurface;
                chunk.PrimaryTexture = LoadTexture(lc.SurfaceTextureId) ?? GetFallbackTexture();
                chunk.SecondaryTexture = LoadTexture(lc.SubsurfaceTextureId);
                chunk.MergeTexture = useBiomeMerge
                    ? LoadTexture(GetBiomeSurfaceTextureId(mergeBiome!))
                    : null;
                return;
            }

            Texture2D? primary = null;
            var primaryTextureId = SelectTextureForChunk(biome, chunk);
            if (!string.IsNullOrWhiteSpace(primaryTextureId))
                primary = LoadTexture(primaryTextureId);
            chunk.PrimaryTexture = primary ?? GetFallbackTexture();
            chunk.SecondaryTexture = null;
            chunk.MergeTexture = useBiomeMerge
                ? LoadTexture(GetBiomeSurfaceTextureId(mergeBiome!))
                : null;
        }

        private static string? GetBiomeSurfaceTextureId(BiomeData biome)
        {
            if (!string.IsNullOrWhiteSpace(biome.TerrainLayers?.SurfaceTextureId))
                return biome.TerrainLayers.SurfaceTextureId;
            if (biome.SurfaceTextures?.Count > 0)
                return biome.SurfaceTextures[0].TextureId;
            return null;
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
            int effectApplies = 0;
            int textureBatches = 0;
            const int MaxDrawCallsPerFrame = 128;
            const int MaxSecondaryDrawCallsPerFrame = 40;

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
            var prevDepth = _graphicsDevice.DepthStencilState;
            var prevRaster = _graphicsDevice.RasterizerState;
            var prevSampler = _graphicsDevice.SamplerStates[0];
            var prevBlend = _graphicsDevice.BlendState;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            _graphicsDevice.SamplerStates[0] = SamplerState.LinearWrap;
            bool wireframe = RuntimeEnvironment.IsDevelopmentEnvironment && _settings.Current.Debug.Wireframe;

            CollectVisibleChunks(pos, frustum, drawDistance);

            var lit = _sky.AmbientColor + _sky.SunColor * _sky.SunIntensity * 0.50f;
            var litDiffuse = new XnaVector3(
                Math.Clamp(lit.X, 0.20f, 1f) * brightness,
                Math.Clamp(lit.Y, 0.20f, 1f) * brightness,
                Math.Clamp(lit.Z, 0.20f, 1f) * brightness);
            var fallbackDiffuse = new XnaVector3(0.7f * brightness, 0.7f * brightness, 0.72f * brightness);

            float secondaryPassDistance = drawDistance * _secondaryPassDistanceScale;
            float layeredCutoffSq = secondaryPassDistance * secondaryPassDistance * 0.25f;
            float crossfadeCutoffSq = secondaryPassDistance * secondaryPassDistance;
            bool crossfadeEnabled = graphics.BiomeTextureCrossfade;

            BuildPrimaryDrawItems(litDiffuse, fallbackDiffuse);
            _drawItemsScratch.Sort();
            DrawBatchedItems(
                _drawItemsScratch,
                TerrainPassKind.Primary,
                BlendState.Opaque,
                wireframe,
                ref drawCalls,
                ref effectApplies,
                ref textureBatches,
                ref chunksDrawn,
                MaxDrawCallsPerFrame);

            BuildMergeDrawItems(crossfadeEnabled, crossfadeCutoffSq, litDiffuse);
            _drawItemsScratch.Sort();
            secondaryPasses += DrawBatchedItems(
                _drawItemsScratch,
                TerrainPassKind.Merge,
                BlendState.NonPremultiplied,
                wireframe,
                ref drawCalls,
                ref effectApplies,
                ref textureBatches,
                ref chunksDrawn,
                MaxDrawCallsPerFrame,
                MaxSecondaryDrawCallsPerFrame);

            BuildLayerDrawItems(layeredCutoffSq, litDiffuse);
            _drawItemsScratch.Sort();
            secondaryPasses += DrawBatchedItems(
                _drawItemsScratch,
                TerrainPassKind.Layer,
                BlendState.NonPremultiplied,
                wireframe,
                ref drawCalls,
                ref effectApplies,
                ref textureBatches,
                ref chunksDrawn,
                MaxDrawCallsPerFrame,
                MaxSecondaryDrawCallsPerFrame);

            _lastChunksDrawn = chunksDrawn;
            _lastDrawCalls = drawCalls;
            _lastSecondaryPasses = secondaryPasses;
            _lastEffectApplies = effectApplies;
            _lastTextureBatches = textureBatches;

            _graphicsDevice.DepthStencilState = prevDepth;
            _graphicsDevice.RasterizerState = prevRaster;
            _graphicsDevice.SamplerStates[0] = prevSampler;
            _graphicsDevice.BlendState = prevBlend;
        }

        private void CollectVisibleChunks(XnaVector3 cameraPos, XnaBoundingFrustum frustum, float drawDistance)
        {
            _visibleChunksScratch.Clear();
            foreach (var key in _activeChunkKeys)
            {
                if (!_chunks.TryGetValue(key, out var chunk)) continue;
                if (!TryGetVisibleChunkState(chunk, cameraPos, frustum, drawDistance, out float distanceSq))
                    continue;
                int rasterTier = GetRasterTier(key.Item3);
                _visibleChunksScratch.Add(new VisibleTerrainChunk(key, distanceSq, rasterTier));
            }
        }

        private void BuildPrimaryDrawItems(XnaVector3 litDiffuse, XnaVector3 fallbackDiffuse)
        {
            _drawItemsScratch.Clear();
            foreach (var visible in _visibleChunksScratch)
            {
                if (!_chunks.TryGetValue(visible.Key, out var chunk)) continue;
                bool isLayered = chunk.UseSplatLayering && chunk.LayerMode != TerrainChunkLayerBlendMode.None;
                if (chunk.PrimaryTexture != null)
                {
                    float mod = isLayered ? 1f : chunk.PrimaryLightingModifier;
                    var diffuse = new XnaVector3(
                        Math.Clamp(litDiffuse.X * mod, 0.20f, 1f),
                        Math.Clamp(litDiffuse.Y * mod, 0.20f, 1f),
                        Math.Clamp(litDiffuse.Z * mod, 0.20f, 1f));
                    _drawItemsScratch.Add(new TerrainDrawItem(
                        visible, chunk.PrimaryTexture, true, diffuse, TerrainPassKind.Primary));
                }
                else
                {
                    _drawItemsScratch.Add(new TerrainDrawItem(
                        visible, null, false, fallbackDiffuse, TerrainPassKind.Primary));
                }
            }
        }

        private void BuildMergeDrawItems(bool crossfadeEnabled, float crossfadeCutoffSq, XnaVector3 litDiffuse)
        {
            _drawItemsScratch.Clear();
            if (!crossfadeEnabled) return;
            foreach (var visible in _visibleChunksScratch)
            {
                if (visible.DistanceSq > crossfadeCutoffSq) continue;
                if (!_chunks.TryGetValue(visible.Key, out var chunk)) continue;
                if (chunk.MergeTexture == null || chunk.BiomeBlendCoverage <= 0.02f) continue;
                _drawItemsScratch.Add(new TerrainDrawItem(
                    visible, chunk.MergeTexture, true, litDiffuse, TerrainPassKind.Merge));
            }
        }

        private void BuildLayerDrawItems(float layeredCutoffSq, XnaVector3 litDiffuse)
        {
            _drawItemsScratch.Clear();
            foreach (var visible in _visibleChunksScratch)
            {
                if (visible.DistanceSq > layeredCutoffSq) continue;
                if (!_chunks.TryGetValue(visible.Key, out var chunk)) continue;
                bool isLayered = chunk.UseSplatLayering && chunk.LayerMode != TerrainChunkLayerBlendMode.None;
                if (!isLayered || chunk.LayerBlendCoverage <= 0.02f || chunk.SecondaryTexture == null) continue;
                _drawItemsScratch.Add(new TerrainDrawItem(
                    visible, chunk.SecondaryTexture, true, litDiffuse, TerrainPassKind.Layer));
            }
        }

        private int DrawBatchedItems(
            List<TerrainDrawItem> items,
            TerrainPassKind pass,
            BlendState blendState,
            bool wireframe,
            ref int drawCalls,
            ref int effectApplies,
            ref int textureBatches,
            ref int chunksDrawn,
            int maxDrawCalls,
            int maxPassDraws = int.MaxValue)
        {
            if (items.Count == 0) return 0;

            int passDraws = 0;
            _graphicsDevice.BlendState = blendState;

            Texture2D? boundTexture = null;
            bool boundTextureEnabled = false;
            uint boundDiffuse = uint.MaxValue;
            int boundRasterTier = -1;
            bool effectDirty = true;
            RasterizerState? boundRasterizer = null;

            for (int i = 0; i < items.Count; i++)
            {
                if (drawCalls >= maxDrawCalls || passDraws >= maxPassDraws) break;
                var item = items[i];
                if (item.Pass != pass) continue;

                uint diffusePacked = PackDiffuse(item.DiffuseColor);
                bool batchChanged = item.Texture != boundTexture
                    || item.TextureEnabled != boundTextureEnabled
                    || diffusePacked != boundDiffuse
                    || item.Visible.RasterTier != boundRasterTier;

                if (batchChanged)
                {
                    boundTexture = item.Texture;
                    boundTextureEnabled = item.TextureEnabled;
                    boundDiffuse = diffusePacked;
                    boundRasterTier = item.Visible.RasterTier;
                    effectDirty = true;
                    boundRasterizer = wireframe
                        ? _wireframeRasterizer
                        : SelectRasterizerForTier(boundRasterTier);
                    _graphicsDevice.RasterizerState = boundRasterizer;
                    textureBatches++;
                }

                if (!_chunks.TryGetValue(item.Visible.Key, out var chunk)) continue;

                if (effectDirty)
                {
                    _basicEffect.TextureEnabled = boundTextureEnabled;
                    _basicEffect.VertexColorEnabled = true;
                    _basicEffect.Texture = boundTexture;
                    _basicEffect.Alpha = 1f;
                    _basicEffect.DiffuseColor = item.DiffuseColor;
                    _terrainEffectPass.Apply();
                    effectApplies++;
                    effectDirty = false;
                }

                _graphicsDevice.SetVertexBuffer(chunk.Vb);
                _graphicsDevice.Indices = chunk.Ib;
                _graphicsDevice.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, chunk.IndexCount / 3);
                drawCalls++;
                passDraws++;
                chunksDrawn++;
            }

            return passDraws;
        }

        private int GetRasterTier(float tileSize)
        {
            if (tileSize <= 1.5f) return 0;
            if (tileSize <= 3f) return 1;
            return 2;
        }

        private RasterizerState SelectRasterizerForTier(int tier)
            => tier switch
            {
                0 => _rasterizerNear,
                1 => _rasterizerMid,
                _ => _rasterizerFar
            };

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
