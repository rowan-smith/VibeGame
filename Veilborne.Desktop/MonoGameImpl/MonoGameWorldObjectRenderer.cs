using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Serilog;
using SharpGLTF.Schema2;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Objects;
using Veilborne.Settings;
using Veilborne.Sky;
using Quaternion = System.Numerics.Quaternion;
using Matrix4x4 = System.Numerics.Matrix4x4;
using NumVec2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using XnaVec3 = Microsoft.Xna.Framework.Vector3;
using XnaVec2 = Microsoft.Xna.Framework.Vector2;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaMathHelper = Microsoft.Xna.Framework.MathHelper;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Veilborne.MonoGameImpl
{
    public class MonoGameWorldObjectRenderer : IWorldObjectRenderer, IRenderSystem
    {
        private readonly EntityRegistry _entities;
        private readonly GraphicsDevice _graphicsDevice;
        private readonly BasicEffect _effect;
        private readonly RasterizerState _wireframeRasterizer;
        private readonly IGameSettingsService _settings;
        private readonly ISkyLightingService _sky;
        private readonly IShadowMapService _shadowMap;
        private readonly ILogger _log = Log.ForContext<MonoGameWorldObjectRenderer>();
        private readonly WorldObjectRenderConfig _renderConfig;
        private int _newModelLoadsThisFrame;
        private CameraComponent _lastCamera;
        private bool _hasLastCamera;

        private class GlbMesh
        {
            public VertexBuffer Vb = null!;
            public IndexBuffer Ib = null!;
            public int IndexCount;
            public XnaColor Color = XnaColor.White;
            public Texture2D? Texture;
        }

        private readonly struct MeshRange
        {
            public MeshRange(int start, int count)
            {
                Start = start;
                Count = count;
            }

            public int Start { get; }
            public int Count { get; }
        }

        private class GlbModel
        {
            public List<GlbMesh> Meshes = new();
            public List<MeshRange> VariantRanges = new();
            public List<ModelBounds> VariantBounds = new();
            public List<float> VariantBaseLiftAtUnitScale = new();
            public bool UseRandomVariantSelection;
        }
        private readonly Dictionary<string, GlbModel?> _modelCache = new();
        private readonly Dictionary<string, Texture2D> _textureCache = new();
        private struct ModelBounds
        {
            public XnaVec3 Min;
            public XnaVec3 Max;
            public bool Valid;
        }
        private readonly Dictionary<string, ModelBounds> _boundsCache = new();
        private readonly Dictionary<string, float> _autoScaleCache = new();
        private readonly Dictionary<string, string> _normalizedPathCache = new();
        private readonly List<DrawCandidate> _drawCandidates = new(512);
        private readonly struct DrawCandidate
        {
            public DrawCandidate(string modelPath, Vector3 position, Quaternion rotation, Vector3 scale, float distanceSq, int variantSeed, bool isFoliage)
            {
                ModelPath = modelPath;
                Position = position;
                Rotation = rotation;
                Scale = scale;
                DistanceSq = distanceSq;
                VariantSeed = variantSeed;
                IsFoliage = isFoliage;
            }

            public string ModelPath { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
            public float DistanceSq { get; }
            public int VariantSeed { get; }
            public bool IsFoliage { get; }
        }

        private readonly HashSet<string> _missingModelWarnings = new(StringComparer.OrdinalIgnoreCase);

        public MonoGameWorldObjectRenderer(EntityRegistry entities, GraphicsDevice graphicsDevice, IGameSettingsService settings, ISkyLightingService sky, IShadowMapService shadowMap,
            ContentManager? _ignored, XnaMatrix _v, XnaMatrix _p, IWorldConfigService worldConfig)
        {
            _entities = entities;
            _graphicsDevice = graphicsDevice;
            _settings = settings;
            _sky = sky;
            _shadowMap = shadowMap;
            _renderConfig = worldConfig.Config.WorldObjectRender;
            _effect = new BasicEffect(graphicsDevice);
            _wireframeRasterizer = new RasterizerState { FillMode = FillMode.WireFrame, CullMode = CullMode.CullCounterClockwiseFace };
        }

        public void Draw()
        {
            CameraComponent cam = default;
            bool hasCamera = false;
            _entities.ForEachWith<CameraComponent>(e =>
            {
                if (hasCamera) return;
                cam = e.GetComponent<CameraComponent>();
                hasCamera = true;
            });
            if (!hasCamera) return;
            Render(cam);
        }

        public void Render(CameraComponent cam)
        {
            _newModelLoadsThisFrame = 0;
            var pos    = new XnaVec3(cam.Position.X, cam.Position.Y, cam.Position.Z);
            var target = new XnaVec3(cam.Target.X,   cam.Target.Y,   cam.Target.Z);
            var up     = new XnaVec3(cam.Up.X,       cam.Up.Y,       cam.Up.Z);
            int vw = _graphicsDevice.Viewport.Width, vh = _graphicsDevice.Viewport.Height;
            float aspect = vw > 0 && vh > 0 ? (float)vw / vh : 16f / 9f;

            var view = XnaMatrix.CreateLookAt(pos, target, up);
            var proj = XnaMatrix.CreatePerspectiveFieldOfView(
                XnaMathHelper.ToRadians(cam.FovY), aspect, 0.1f, 5000f);
            var frustum = new BoundingFrustum(view * proj);

            var prevDepth  = _graphicsDevice.DepthStencilState;
            var prevRaster = _graphicsDevice.RasterizerState;
            _graphicsDevice.DepthStencilState = DepthStencilState.Default;
            bool wireframe = RuntimeEnvironment.IsDevelopmentEnvironment && _settings.Current.Debug.Wireframe;
            var graphics = _settings.Current.Graphics;
            float renderDistanceScale = graphics.ObjectViewDistance / 100f;
            float drawDistance = _renderConfig.MaxDrawDistance * renderDistanceScale;
            float maxDrawDistanceSq = drawDistance * drawDistance;
            float foliageDrawDistance = drawDistance * Math.Clamp(_renderConfig.FoliageDrawDistanceMultiplier, 0.1f, 1.0f);
            float frustumCullingDistanceSq = _renderConfig.FrustumCullingNearDistance * _renderConfig.FrustumCullingNearDistance;
            bool cameraMoving = _hasLastCamera &&
                                Vector3.DistanceSquared(cam.Position, _lastCamera.Position) > (0.025f * 0.025f);
            if (cameraMoving)
                foliageDrawDistance *= Math.Clamp(_renderConfig.MovingFoliageDrawDistanceMultiplier, 0.1f, 1.0f);
            float foliageDrawDistanceSq = foliageDrawDistance * foliageDrawDistance;
            int frameTreeBudget = cameraMoving
                ? (int)MathF.Max(120, _renderConfig.MaxDetailedObjectsPerFrame * _renderConfig.MovingFrameBudgetScale)
                : Math.Max(1, _renderConfig.MaxDetailedObjectsPerFrame);
            _lastCamera = cam;
            _hasLastCamera = true;
            _graphicsDevice.RasterizerState = wireframe
                ? _wireframeRasterizer
                : RasterizerState.CullNone;
            _drawCandidates.Clear();
            _entities.ForEachWith<RenderComponent, WorldObjectComponent>(entity =>
            {
                if (!entity.TryGetComponent<TransformComponent>(out var transform)) return;
                if (!entity.TryGetComponent<RenderComponent>(out var render)) return;
                if (!render.Visible || string.IsNullOrWhiteSpace(render.ModelPath)) return;

                var dp = transform.Position - cam.Position;
                var distSq = dp.LengthSquared();
                if (distSq > maxDrawDistanceSq) return;
                if (render.IsFoliage && distSq > foliageDrawDistanceSq) return;

                string normalizedPath = NormalizeModelPath(render.ModelPath);
                float autoScale = GetAutoScale(normalizedPath);
                var effectiveScale = ClampScaleToWorldExtent(normalizedPath, SanitizeScale(transform.Scale * autoScale));
                if (distSq >= frustumCullingDistanceSq &&
                    TryGetBoundsSphere(normalizedPath, transform.Position, effectiveScale, out var sphere) &&
                    !frustum.Intersects(sphere))
                    return;

                _drawCandidates.Add(new DrawCandidate(
                    normalizedPath,
                    transform.Position,
                    transform.Rotation,
                    effectiveScale,
                    distSq,
                    entity.Id,
                    render.IsFoliage));
            });

            _drawCandidates.Sort(static (a, b) =>
            {
                int foliageOrder = a.IsFoliage.CompareTo(b.IsFoliage);
                if (foliageOrder != 0) return foliageOrder;
                return a.DistanceSq.CompareTo(b.DistanceSq);
            });
            int drawCount = Math.Min(frameTreeBudget, _drawCandidates.Count);
            int modelLoadBudget = cameraMoving ? _renderConfig.MaxNewModelLoadsWhileMoving : _renderConfig.MaxNewModelLoadsPerFrame;
            for (int i = 0; i < drawCount; i++)
            {
                var c = _drawCandidates[i];
                DrawModel(c.ModelPath, c.Position, c.Rotation, c.Scale, c.VariantSeed, view, proj, modelLoadBudget);
            }

            _graphicsDevice.DepthStencilState = prevDepth;
            _graphicsDevice.RasterizerState   = prevRaster;
        }

        public void DrawWorldObject(SpawnedObject obj)
        {
            // Not used during normal render loop
        }

        // ── Internal ───────────────────────────────────────────────────────────

        private void DrawModel(string modelPath, Vector3 pos, Quaternion rot, Vector3 scale, int variantSeed,
            XnaMatrix view, XnaMatrix proj, int modelLoadBudget)
        {
            if (string.IsNullOrEmpty(modelPath)) return;

            string normalizedPath = NormalizeModelPath(modelPath);
            if (!_modelCache.TryGetValue(normalizedPath, out var model))
            {
                if (_newModelLoadsThisFrame >= modelLoadBudget)
                    return;
                model = LoadGlb(normalizedPath);
                _newModelLoadsThisFrame++;
            }

            if (model == null || model.Meshes.Count == 0)
            {
                if (IsLikelyTreePath(normalizedPath) && _missingModelWarnings.Add(normalizedPath))
                    _log.Warning("Tree model is not renderable (missing or zero meshes): {ModelPath}", normalizedPath);
                return;
            }
            if (!IsFinite(scale)) return;

            int start = 0;
            int count = model.Meshes.Count;
            int variantIndex = -1;
            if (model.UseRandomVariantSelection && model.VariantRanges.Count > 1)
            {
                variantIndex = PositiveModulo(variantSeed, model.VariantRanges.Count);
                var range = model.VariantRanges[variantIndex];
                start = range.Start;
                count = range.Count;
            }
            var qFinal = rot;
            float baseOffset = GetVariantBaseLift(model, normalizedPath, variantIndex, scale);
            if (baseOffset < 0f)
                baseOffset = 0f;
            var variantBounds = GetVariantBounds(model, normalizedPath, variantIndex);
            float variantCenterX = (variantBounds.Min.X + variantBounds.Max.X) * 0.5f;
            float variantCenterZ = (variantBounds.Min.Z + variantBounds.Max.Z) * 0.5f;
            var localCenterOffset = new Vector3(-variantCenterX * scale.X, 0f, -variantCenterZ * scale.Z);
            var worldCenterOffset = Vector3.Transform(localCenterOffset, qFinal);

            var world =
                XnaMatrix.CreateScale(new XnaVec3(scale.X, scale.Y, scale.Z)) *
                XnaMatrix.CreateFromQuaternion(
                    new Microsoft.Xna.Framework.Quaternion(qFinal.X, qFinal.Y, qFinal.Z, qFinal.W)) *
                XnaMatrix.CreateTranslation(new XnaVec3(pos.X + worldCenterOffset.X, pos.Y + baseOffset, pos.Z + worldCenterOffset.Z));

            _effect.World      = world;
            _effect.View       = view;
            _effect.Projection = proj;
            _effect.LightingEnabled = true;
            _effect.PreferPerPixelLighting = false;
            _effect.EnableDefaultLighting();
            var sunDir = _sky.SunDirection;
            _effect.DirectionalLight0.Direction = new XnaVec3(sunDir.X, sunDir.Y, sunDir.Z);
            _effect.DirectionalLight1.Enabled = false;
            _effect.DirectionalLight2.Enabled = false;
            float brightness = _settings.Current.Graphics.Brightness / 100f;
            var ambient = _sky.AmbientColor;
            var sun = _sky.SunColor * _sky.SunIntensity;
            float shadowSample = _shadowMap.IsReady ? _shadowMap.SampleShadow(pos) : 1f;
            float shadowTerm = Math.Clamp(1f - _sky.ShadowStrength * 0.60f * (1f - shadowSample), 0.30f, 1f);
            _effect.AmbientLightColor = new XnaVec3(ambient.X, ambient.Y, ambient.Z) * brightness * shadowTerm;
            _effect.DirectionalLight0.DiffuseColor = new XnaVec3(sun.X, sun.Y, sun.Z) * brightness;
            _effect.DirectionalLight0.SpecularColor = new XnaVec3(0.10f, 0.10f, 0.10f);
            _effect.VertexColorEnabled  = false;

            int end = Math.Min(model.Meshes.Count, start + count);
            for (int i = start; i < end; i++)
            {
                var mesh = model.Meshes[i];
                _effect.DiffuseColor = new Microsoft.Xna.Framework.Vector3(1f, 1f, 1f);
                float meshAlpha = mesh.Color.A / 255f;
                if (meshAlpha < 0.2f)
                    meshAlpha = 1f;
                _effect.Alpha = Math.Clamp(meshAlpha, 0.05f, 1f);
                _effect.TextureEnabled = mesh.Texture != null;
                _effect.Texture = mesh.Texture;

                _graphicsDevice.SetVertexBuffer(mesh.Vb);
                _graphicsDevice.Indices = mesh.Ib;

                foreach (var pass in _effect.CurrentTechnique.Passes)
                {
                    pass.Apply();
                    _graphicsDevice.DrawIndexedPrimitives(
                        Microsoft.Xna.Framework.Graphics.PrimitiveType.TriangleList, 0, 0, mesh.IndexCount / 3);
                }
            }
        }

        private GlbModel? LoadGlb(string relativePath)
        {
            // Resolve full path relative to executable
            string fullPath = Path.IsPathRooted(relativePath)
                ? relativePath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, relativePath);
            if (!File.Exists(fullPath))
            {
                // Try a lower-case filename fallback because model refs/configs and file names differ by case.
                string? dir = Path.GetDirectoryName(fullPath);
                string? file = Path.GetFileName(fullPath);
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(file) && Directory.Exists(dir))
                {
                    string? alt = Directory
                        .EnumerateFiles(dir, "*.glb")
                        .FirstOrDefault(f => string.Equals(Path.GetFileName(f), file, StringComparison.OrdinalIgnoreCase));
                    if (alt != null) fullPath = alt;
                }
            }
            if (!File.Exists(fullPath))
            {
                _log.Warning("GLB model not found: {Path}", fullPath);
                _modelCache[relativePath] = null;
                return null;
            }

            try
            {
                var modelRoot = ModelRoot.Load(fullPath);

                var glbModel = new GlbModel();
                var overallBounds = CreateEmptyBounds();

                // Group mesh-bearing nodes by scene-root ancestor to detect
                // multi-model GLBs (e.g. Trees.glb containing several tree variants).
                var variantGroups = GroupNodesBySceneRoot(modelRoot);
                var processedNodes = new HashSet<int>();

                foreach (var groupNodes in variantGroups)
                {
                    int variantStart = glbModel.Meshes.Count;
                    var variantBounds = CreateEmptyBounds();

                foreach (var node in groupNodes)
                {
                    if (processedNodes.Contains(node.LogicalIndex)) continue;
                    processedNodes.Add(node.LogicalIndex);
                    Matrix4x4 nodeMatrix = node.WorldMatrix;
                    Matrix4x4.Invert(nodeMatrix, out var nodeInverse);
                    Matrix4x4 nodeNormalMatrix = Matrix4x4.Transpose(nodeInverse);

                    foreach (var primitive in node.Mesh.Primitives)
                    {
                        if (!primitive.VertexAccessors.TryGetValue("POSITION", out var posAccessor)) continue;
                        var positions = posAccessor.AsVector3Array();
                        int vertexCount = positions.Count;
                        if (vertexCount == 0) continue;

                        primitive.VertexAccessors.TryGetValue("NORMAL", out var normalAccessor);
                        primitive.VertexAccessors.TryGetValue("TEXCOORD_0", out var uvAccessor);
                        var normals = normalAccessor != null ? normalAccessor.AsVector3Array() : null;
                        var uvs = uvAccessor != null ? uvAccessor.AsVector2Array() : null;

                        IEnumerable<uint> rawIndices = primitive.IndexAccessor != null
                            ? primitive.IndexAccessor.AsIndicesArray()
                            : Enumerable.Range(0, vertexCount).Select(i => (uint)i);

                        var idxList = new List<int>();
                        foreach (var idx in rawIndices)
                        {
                            if (idx < vertexCount) idxList.Add((int)idx);
                        }

                        int triangleIndexCount = idxList.Count - (idxList.Count % 3);
                        if (triangleIndexCount == 0) continue;
                        if (triangleIndexCount != idxList.Count) idxList.RemoveRange(triangleIndexCount, idxList.Count - triangleIndexCount);

                        var verts = new VertexPositionNormalTexture[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                        {
                            var p = Vector3.Transform(positions[i], nodeMatrix);
                            var n = normals != null && i < normals.Count
                                ? Vector3.TransformNormal(normals[i], nodeNormalMatrix)
                                : Vector3.UnitY;
                            if (n.LengthSquared() > 1e-8f)
                                n = Vector3.Normalize(n);
                            else
                                n = Vector3.UnitY;
                            var uv = uvs != null && i < uvs.Count ? uvs[i] : NumVec2.Zero;

                            if (p.X < variantBounds.Min.X) variantBounds.Min.X = p.X;
                            if (p.Y < variantBounds.Min.Y) variantBounds.Min.Y = p.Y;
                            if (p.Z < variantBounds.Min.Z) variantBounds.Min.Z = p.Z;
                            if (p.X > variantBounds.Max.X) variantBounds.Max.X = p.X;
                            if (p.Y > variantBounds.Max.Y) variantBounds.Max.Y = p.Y;
                            if (p.Z > variantBounds.Max.Z) variantBounds.Max.Z = p.Z;
                            variantBounds.Valid = true;
                            if (p.X < overallBounds.Min.X) overallBounds.Min.X = p.X;
                            if (p.Y < overallBounds.Min.Y) overallBounds.Min.Y = p.Y;
                            if (p.Z < overallBounds.Min.Z) overallBounds.Min.Z = p.Z;
                            if (p.X > overallBounds.Max.X) overallBounds.Max.X = p.X;
                            if (p.Y > overallBounds.Max.Y) overallBounds.Max.Y = p.Y;
                            if (p.Z > overallBounds.Max.Z) overallBounds.Max.Z = p.Z;
                            overallBounds.Valid = true;

                            verts[i] = new VertexPositionNormalTexture(
                                new XnaVec3(p.X, p.Y, p.Z),
                                new XnaVec3(n.X, n.Y, n.Z),
                                new XnaVec2(uv.X, 1f - uv.Y));
                        }

                        var vb = new VertexBuffer(_graphicsDevice, typeof(VertexPositionNormalTexture),
                            verts.Length, BufferUsage.WriteOnly);
                        vb.SetData(verts);

                        IndexBuffer ib;
                        if (verts.Length <= 65535)
                        {
                            var indices16 = idxList.ConvertAll(i => (ushort)i).ToArray();
                            ib = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits,
                                indices16.Length, BufferUsage.WriteOnly);
                            ib.SetData(indices16);
                        }
                        else
                        {
                            ib = new IndexBuffer(_graphicsDevice, IndexElementSize.ThirtyTwoBits,
                                idxList.Count, BufferUsage.WriteOnly);
                            ib.SetData(idxList.ToArray());
                        }

                        XnaColor meshColor = XnaColor.White;
                        Texture2D? meshTexture = null;
                        if (primitive.Material != null)
                        {
                            foreach (var channel in primitive.Material.Channels)
                            {
                                if (channel.Key != "BaseColor") continue;

                                var c = channel.Color;
                                meshColor = new XnaColor(c.X, c.Y, c.Z, c.W);

                                if (channel.Texture?.PrimaryImage != null)
                                {
                                    string texKey = $"{fullPath}::{channel.Texture.PrimaryImage.LogicalIndex}";
                                    if (!_textureCache.TryGetValue(texKey, out var tex))
                                    {
                                        var bytes = channel.Texture.PrimaryImage.Content.Content.ToArray();
                                        using var ms = new MemoryStream(bytes, writable: false);
                                        tex = Texture2D.FromStream(_graphicsDevice, ms);
                                        _textureCache[texKey] = tex;
                                    }

                                    meshTexture = tex;
                                }

                                break;
                            }
                        }

                        glbModel.Meshes.Add(new GlbMesh
                        {
                            Vb = vb, Ib = ib,
                            IndexCount = idxList.Count,
                            Color = meshColor,
                            Texture = meshTexture
                        });
                    }
                }

                    int variantCount = glbModel.Meshes.Count - variantStart;
                    if (variantCount > 0)
                    {
                        glbModel.VariantRanges.Add(new MeshRange(variantStart, variantCount));
                        glbModel.VariantBounds.Add(variantBounds);
                    }
                }

                glbModel.UseRandomVariantSelection = glbModel.VariantRanges.Count > 1;

                for (int i = 0; i < glbModel.VariantBounds.Count; i++)
                {
                    var vb = glbModel.VariantBounds[i];
                    glbModel.VariantBaseLiftAtUnitScale.Add(ComputeBaseLift(vb, Vector3.One));
                }

                // For multi-model GLBs use the largest variant for auto-scale & culling
                ModelBounds representativeBounds = overallBounds;
                if (glbModel.VariantBounds.Count > 1)
                {
                    float maxDim = 0f;
                    foreach (var vb in glbModel.VariantBounds)
                    {
                        if (!vb.Valid) continue;
                        float dim = MathF.Max(MathF.Abs(vb.Max.X - vb.Min.X),
                                    MathF.Max(MathF.Abs(vb.Max.Y - vb.Min.Y),
                                              MathF.Abs(vb.Max.Z - vb.Min.Z)));
                        if (dim > maxDim) { maxDim = dim; representativeBounds = vb; }
                    }
                }

                _log.Debug(
                    "Loaded GLB '{Path}': nodes={NodeCount}, meshes={MeshCount}, variants={VariantCount}",
                    relativePath,
                    modelRoot.LogicalNodes.Count,
                    glbModel.Meshes.Count,
                    glbModel.VariantRanges.Count);
                if (glbModel.Meshes.Count == 0)
                    _log.Warning("Loaded GLB has no renderable meshes: {Path}", relativePath);
                _modelCache[relativePath] = glbModel;
                _boundsCache[relativePath] = representativeBounds;
                _autoScaleCache[relativePath] = ComputeAutoScale(representativeBounds, relativePath);
                return glbModel;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Failed to load GLB: {Path}", fullPath);
                _modelCache[relativePath] = null;
                _boundsCache.Remove(relativePath);
                return null;
            }
        }

        private string NormalizeModelPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            if (_normalizedPathCache.TryGetValue(path, out var cached)) return cached;
            string p = path.Replace('\\', Path.DirectorySeparatorChar)
                           .Replace('/', Path.DirectorySeparatorChar);
            _normalizedPathCache[path] = p;
            return p;
        }

        private static bool IsLikelyTreePath(string modelPath)
            => modelPath.Contains("tree", StringComparison.OrdinalIgnoreCase) ||
               modelPath.Contains("maple", StringComparison.OrdinalIgnoreCase) ||
               modelPath.Contains("birch", StringComparison.OrdinalIgnoreCase) ||
               modelPath.Contains("pine", StringComparison.OrdinalIgnoreCase) ||
               modelPath.Contains("palm", StringComparison.OrdinalIgnoreCase);

        private ModelBounds GetVariantBounds(GlbModel model, string key, int variantIndex)
        {
            if (variantIndex >= 0 && variantIndex < model.VariantBounds.Count && model.VariantBounds[variantIndex].Valid)
                return model.VariantBounds[variantIndex];
            if (_boundsCache.TryGetValue(key, out var bounds) && bounds.Valid)
                return bounds;
            return default;
        }

        private float GetVariantBaseLift(GlbModel model, string modelPath, int variantIndex, Vector3 scale)
        {
            if (variantIndex >= 0 && variantIndex < model.VariantBaseLiftAtUnitScale.Count)
            {
                float uniformScale = (MathF.Abs(scale.X) + MathF.Abs(scale.Y) + MathF.Abs(scale.Z)) / 3f;
                return model.VariantBaseLiftAtUnitScale[variantIndex] * MathF.Max(0.01f, uniformScale);
            }
            return ComputeBaseLift(GetVariantBounds(model, modelPath, variantIndex), scale);
        }

        private float ComputeBaseLift(ModelBounds bounds, Vector3 scale)
        {
            if (!bounds.Valid)
                return 0f;
            float minY = bounds.Min.Y * MathF.Abs(scale.Y);
            float maxLift = MathF.Max(0.1f, _renderConfig.MaxBaseLiftMeters);
            return Math.Clamp(-minY, -maxLift, maxLift);
        }

        private bool TryGetBoundsSphere(string key, Vector3 position, Vector3 scale, out BoundingSphere sphere)
        {
            sphere = default;
            if (!_boundsCache.TryGetValue(key, out var bounds) || !bounds.Valid) return false;

            var ext = (bounds.Max - bounds.Min) * 0.5f;
            var baseRadius = MathF.Sqrt(ext.X * ext.X + ext.Y * ext.Y + ext.Z * ext.Z);
            var s = MathF.Max(MathF.Abs(scale.X), MathF.Max(MathF.Abs(scale.Y), MathF.Abs(scale.Z)));
            var radius = MathF.Max(1f, baseRadius * MathF.Max(0.001f, s));
            sphere = new BoundingSphere(new XnaVec3(position.X, position.Y, position.Z), radius);
            return true;
        }

        private float GetAutoScale(string key)
        {
            if (_autoScaleCache.TryGetValue(key, out var scale))
                return scale;
            return 1f;
        }

        private float ComputeAutoScale(ModelBounds bounds, string modelKey)
        {
            if (!bounds.Valid) return 1f;
            float dx = MathF.Abs(bounds.Max.X - bounds.Min.X);
            float dy = MathF.Abs(bounds.Max.Y - bounds.Min.Y);
            float dz = MathF.Abs(bounds.Max.Z - bounds.Min.Z);
            float maxDim = MathF.Max(dx, MathF.Max(dy, dz));
            if (maxDim <= _renderConfig.MaxReasonableModelDimension)
                return 1f;

            float target = MathF.Max(0.25f, _renderConfig.AutoNormalizedTargetDimension);
            float scale = Math.Clamp(target / MathF.Max(0.01f, maxDim), 0.02f, 1f);
            _log.Warning(
                "Auto-normalized oversized world model {Model} (maxDim={MaxDim:0.##}) with scale {Scale:0.###}",
                modelKey, maxDim, scale);
            return scale;
        }

        private static Vector3 SanitizeScale(Vector3 scale)
        {
            float x = Math.Clamp(float.IsFinite(scale.X) ? scale.X : 1f, 0.005f, 8f);
            float y = Math.Clamp(float.IsFinite(scale.Y) ? scale.Y : 1f, 0.005f, 8f);
            float z = Math.Clamp(float.IsFinite(scale.Z) ? scale.Z : 1f, 0.005f, 8f);
            return new Vector3(x, y, z);
        }

        private Vector3 ClampScaleToWorldExtent(string key, Vector3 scale)
        {
            if (!_boundsCache.TryGetValue(key, out var bounds) || !bounds.Valid)
                return scale;

            float dx = MathF.Abs(bounds.Max.X - bounds.Min.X) * MathF.Abs(scale.X);
            float dy = MathF.Abs(bounds.Max.Y - bounds.Min.Y) * MathF.Abs(scale.Y);
            float dz = MathF.Abs(bounds.Max.Z - bounds.Min.Z) * MathF.Abs(scale.Z);
            float maxDim = MathF.Max(dx, MathF.Max(dy, dz));
            if (maxDim <= _renderConfig.MaxFinalWorldObjectDimension || maxDim <= 1e-4f)
                return scale;

            float down = Math.Clamp(_renderConfig.MaxFinalWorldObjectDimension / maxDim, 0.02f, 1f);
            return new Vector3(scale.X * down, scale.Y * down, scale.Z * down);
        }

        private static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        }

        private static int PositiveModulo(int value, int modulo)
        {
            if (modulo <= 0) return 0;
            int result = value % modulo;
            return result < 0 ? result + modulo : result;
        }

        private static ModelBounds CreateEmptyBounds()
        {
            return new ModelBounds
            {
                Min = new XnaVec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
                Max = new XnaVec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity),
                Valid = false
            };
        }

        /// <summary>
        /// Groups mesh-bearing nodes by their scene-root ancestor so that
        /// multi-model GLBs (e.g. Trees.glb with several tree variants)
        /// produce one variant per top-level group.
        /// </summary>
        private static List<List<SharpGLTF.Schema2.Node>> GroupNodesBySceneRoot(ModelRoot modelRoot)
        {
            var meshNodes = modelRoot.LogicalNodes.Where(n => n.Mesh != null).ToList();
            if (meshNodes.Count == 0)
                return new List<List<SharpGLTF.Schema2.Node>>();

            var sceneRoots = (modelRoot.DefaultScene?.VisualChildren
                              ?? Enumerable.Empty<SharpGLTF.Schema2.Node>()).ToList();

            // Try grouping by top-level scene children
            if (sceneRoots.Count > 1)
            {
                var result = GroupMeshNodesByAncestorSet(meshNodes, sceneRoots);
                if (result.Count > 1)
                    return result;
            }

            // Single scene root — try grouping by its direct children instead
            if (sceneRoots.Count == 1)
            {
                var secondLevel = sceneRoots[0].VisualChildren.ToList();
                if (secondLevel.Count > 1)
                {
                    var result = GroupMeshNodesByAncestorSet(meshNodes, secondLevel);
                    if (result.Count > 1)
                        return result;
                }
            }

            // If each mesh node is a direct scene child with no shared hierarchy,
            // and there are multiple, treat each as a variant
            if (meshNodes.Count > 1 && meshNodes.All(n => sceneRoots.Any(r => r.LogicalIndex == n.LogicalIndex)))
                return meshNodes.Select(n => new List<SharpGLTF.Schema2.Node> { n }).ToList();

            return new List<List<SharpGLTF.Schema2.Node>> { meshNodes };
        }

        private static List<List<SharpGLTF.Schema2.Node>> GroupMeshNodesByAncestorSet(
            List<SharpGLTF.Schema2.Node> meshNodes, List<SharpGLTF.Schema2.Node> ancestors)
        {
            var ancestorSet = new HashSet<int>(ancestors.Select(r => r.LogicalIndex));
            var groups = new Dictionary<int, List<SharpGLTF.Schema2.Node>>();

            foreach (var node in meshNodes)
            {
                var current = node;
                while (current.VisualParent != null && !ancestorSet.Contains(current.LogicalIndex))
                    current = current.VisualParent;

                int rootKey = current.LogicalIndex;
                if (!groups.TryGetValue(rootKey, out var list))
                {
                    list = new List<SharpGLTF.Schema2.Node>();
                    groups[rootKey] = list;
                }
                list.Add(node);
            }

            if (groups.Count <= 1)
                return new List<List<SharpGLTF.Schema2.Node>> { meshNodes };

            return groups.OrderBy(g => g.Key).Select(g => g.Value).ToList();
        }
    }
}
