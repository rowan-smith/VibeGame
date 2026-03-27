using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Serilog;
using SharpGLTF.Memory;
using SharpGLTF.Schema2;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Settings;
using Veilborne.Core.Sky;
using Veilborne.Objects;
using Quaternion = System.Numerics.Quaternion;
using Matrix4x4 = System.Numerics.Matrix4x4;
using NumVec2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using XnaVec3 = Microsoft.Xna.Framework.Vector3;
using XnaVec2 = Microsoft.Xna.Framework.Vector2;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaMathHelper = Microsoft.Xna.Framework.MathHelper;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Veilborne.Core.MonoGameImpl
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
        private const float MaxTreeDrawDistance = 90f;
        private const int MaxDetailedTreesPerFrame = 600;
        private const int MaxNewModelLoadsPerFrame = 1;
        private const float FrustumCullingDistance = 1f;
        private const float MovementFrameBudgetScale = 0.70f;
        private const float MaxReasonableModelDimension = 25f;
        private const float AutoNormalizedTargetDimension = 6f;
        private const float MaxBaseLiftMeters = 12f;
        private const float MaxFinalWorldObjectDimension = 14f;
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

        private class GlbModel { public List<GlbMesh> Meshes = new(); }
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
        private readonly struct DrawCandidate
        {
            public DrawCandidate(string modelPath, Vector3 position, Quaternion rotation, Vector3 scale, float distanceSq)
            {
                ModelPath = modelPath;
                Position = position;
                Rotation = rotation;
                Scale = scale;
                DistanceSq = distanceSq;
            }

            public string ModelPath { get; }
            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Scale { get; }
            public float DistanceSq { get; }
        }

        public MonoGameWorldObjectRenderer(EntityRegistry entities, GraphicsDevice graphicsDevice, IGameSettingsService settings, ISkyLightingService sky, IShadowMapService shadowMap,
            ContentManager? _ignored, XnaMatrix _v, XnaMatrix _p)
        {
            _entities = entities;
            _graphicsDevice = graphicsDevice;
            _settings = settings;
            _sky = sky;
            _shadowMap = shadowMap;
            _effect = new BasicEffect(graphicsDevice);
            _wireframeRasterizer = new RasterizerState { FillMode = FillMode.WireFrame, CullMode = CullMode.CullCounterClockwiseFace };
        }

        public void Draw()
        {
            _newModelLoadsThisFrame = 0;
            // Find a player camera for view/projection
            CameraComponent cam = default;
            bool hasCamera = false;
            foreach (var e in _entities.GetEntitiesWith<CameraComponent>())
            {
                cam = e.GetComponent<CameraComponent>();
                hasCamera = true;
                break;
            }
            if (!hasCamera) return;

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
            float renderDistanceScale = graphics.RenderDistance / 100f;
            float drawDistance = MaxTreeDrawDistance * renderDistanceScale;
            float maxDrawDistanceSq = drawDistance * drawDistance;
            float frustumCullingDistanceSq = FrustumCullingDistance * FrustumCullingDistance;
            bool cameraMoving = _hasLastCamera &&
                                Vector3.DistanceSquared(cam.Position, _lastCamera.Position) > (0.025f * 0.025f);
            int frameTreeBudget = cameraMoving
                ? (int)MathF.Max(120, MaxDetailedTreesPerFrame * MovementFrameBudgetScale)
                : MaxDetailedTreesPerFrame;
            _lastCamera = cam;
            _hasLastCamera = true;
            _graphicsDevice.RasterizerState = wireframe
                ? _wireframeRasterizer
                : RasterizerState.CullCounterClockwise;
            var candidates = new List<DrawCandidate>(512);
            foreach (var entity in _entities.GetEntitiesWith<RenderComponent, WorldObjectComponent>())
            {
                if (!entity.TryGetComponent<TransformComponent>(out var transform)) continue;
                if (!entity.TryGetComponent<RenderComponent>(out var render)) continue;
                if (!render.Visible || string.IsNullOrWhiteSpace(render.ModelPath)) continue;

                var dp = transform.Position - cam.Position;
                var distSq = dp.LengthSquared();
                if (distSq > maxDrawDistanceSq) continue;

                string normalizedPath = NormalizeModelPath(render.ModelPath);
                float autoScale = GetAutoScale(normalizedPath);
                var effectiveScale = ClampScaleToWorldExtent(normalizedPath, SanitizeScale(transform.Scale * autoScale));
                if (distSq >= frustumCullingDistanceSq &&
                    TryGetBoundsSphere(normalizedPath, transform.Position, effectiveScale, out var sphere) &&
                    !frustum.Intersects(sphere))
                    continue;

                candidates.Add(new DrawCandidate(
                    render.ModelPath,
                    transform.Position,
                    transform.Rotation,
                    effectiveScale,
                    distSq));
            }

            candidates.Sort(static (a, b) => a.DistanceSq.CompareTo(b.DistanceSq));
            int drawCount = Math.Min(frameTreeBudget, candidates.Count);
            for (int i = 0; i < drawCount; i++)
            {
                var c = candidates[i];
                DrawModel(c.ModelPath, c.Position, c.Rotation, c.Scale, view, proj);
            }

            _graphicsDevice.DepthStencilState = prevDepth;
            _graphicsDevice.RasterizerState   = prevRaster;
        }

        public void DrawWorldObject(SpawnedObject obj)
        {
            // Not used during normal render loop
        }

        // ── Internal ───────────────────────────────────────────────────────────

        private void DrawModel(string modelPath, Vector3 pos, Quaternion rot, Vector3 scale,
            XnaMatrix view, XnaMatrix proj)
        {
            if (string.IsNullOrEmpty(modelPath)) return;

            string normalizedPath = NormalizeModelPath(modelPath);
            if (!_modelCache.TryGetValue(normalizedPath, out var model))
            {
                if (_newModelLoadsThisFrame >= MaxNewModelLoadsPerFrame)
                    return;
                model = LoadGlb(normalizedPath);
                _newModelLoadsThisFrame++;
            }

            if (model == null || model.Meshes.Count == 0) return;
            if (!IsFinite(scale)) return;

            var qCorrection = GetAxisCorrection(normalizedPath, scale);
            var qFinal = Quaternion.Normalize(Quaternion.Concatenate(rot, qCorrection));
            float baseOffset = ComputeBaseLift(normalizedPath, scale, qFinal);

            var world =
                XnaMatrix.CreateScale(new XnaVec3(scale.X, scale.Y, scale.Z)) *
                XnaMatrix.CreateFromQuaternion(
                    new Microsoft.Xna.Framework.Quaternion(qFinal.X, qFinal.Y, qFinal.Z, qFinal.W)) *
                XnaMatrix.CreateTranslation(new XnaVec3(pos.X, pos.Y + baseOffset, pos.Z));

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

            foreach (var mesh in model.Meshes)
            {
                _effect.DiffuseColor = new Microsoft.Xna.Framework.Vector3(1f, 1f, 1f);
                _effect.Alpha = mesh.Color.A / 255f;
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
                var b = new ModelBounds
                {
                    Min = new XnaVec3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
                    Max = new XnaVec3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity),
                    Valid = false
                };

                foreach (var node in modelRoot.LogicalNodes)
                {
                    if (node.Mesh == null) continue;

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
                            var p = positions[i];
                            var n = normals != null && i < normals.Count
                                ? Vector3.Normalize(normals[i])
                                : Vector3.UnitY;
                            var uv = uvs != null && i < uvs.Count ? uvs[i] : NumVec2.Zero;

                            if (p.X < b.Min.X) b.Min.X = p.X;
                            if (p.Y < b.Min.Y) b.Min.Y = p.Y;
                            if (p.Z < b.Min.Z) b.Min.Z = p.Z;
                            if (p.X > b.Max.X) b.Max.X = p.X;
                            if (p.Y > b.Max.Y) b.Max.Y = p.Y;
                            if (p.Z > b.Max.Z) b.Max.Z = p.Z;
                            b.Valid = true;

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
                            var shorts = idxList.ConvertAll(i => (short)i).ToArray();
                            ib = new IndexBuffer(_graphicsDevice, IndexElementSize.SixteenBits,
                                shorts.Length, BufferUsage.WriteOnly);
                            ib.SetData(shorts);
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

                _log.Debug("Loaded GLB '{Path}': {MeshCount} meshes", relativePath, glbModel.Meshes.Count);
                _modelCache[relativePath] = glbModel;
                _boundsCache[relativePath] = b;
                _autoScaleCache[relativePath] = ComputeAutoScale(b, relativePath);
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

        private static string NormalizeModelPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string p = path.Replace('\\', Path.DirectorySeparatorChar)
                           .Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(p)) return p;
            return p;
        }

        private Quaternion GetAxisCorrection(string key, Vector3 scale)
        {
            if (!_boundsCache.TryGetValue(key, out var bounds) || !bounds.Valid)
                return Quaternion.Identity;

            var qY = Quaternion.Identity;
            var qZUp = Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2f);
            var qXUp = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2f);

            float eY = EvalYExtent(bounds, scale, qY, out _);
            float eZ = EvalYExtent(bounds, scale, qZUp, out _);
            float eX = EvalYExtent(bounds, scale, qXUp, out _);

            const float bias = 1.1f;
            float best = eY;
            var qBest = qY;
            if (eZ > best * (qBest == qY ? bias : 1f)) { best = eZ; qBest = qZUp; }
            if (eX > best * (qBest == qY ? bias : 1f)) { best = eX; qBest = qXUp; }

            return qBest;
        }

        private float ComputeBaseLift(string key, Vector3 scale, Quaternion q)
        {
            if (!_boundsCache.TryGetValue(key, out var bounds) || !bounds.Valid)
                return 0f;
            EvalYExtent(bounds, scale, q, out float minY);
            return Math.Clamp(-minY, -MaxBaseLiftMeters, MaxBaseLiftMeters);
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
            if (maxDim <= MaxReasonableModelDimension)
                return 1f;

            float scale = Math.Clamp(AutoNormalizedTargetDimension / MathF.Max(0.01f, maxDim), 0.02f, 1f);
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
            if (maxDim <= MaxFinalWorldObjectDimension || maxDim <= 1e-4f)
                return scale;

            float down = Math.Clamp(MaxFinalWorldObjectDimension / maxDim, 0.02f, 1f);
            return new Vector3(scale.X * down, scale.Y * down, scale.Z * down);
        }

        private static bool IsFinite(Vector3 v)
        {
            return float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
        }

        private static float EvalYExtent(ModelBounds bounds, Vector3 scale, Quaternion q, out float minY)
        {
            var min = bounds.Min;
            var max = bounds.Max;
            Span<Vector3> corners = stackalloc Vector3[8]
            {
                new(min.X, min.Y, min.Z), new(max.X, min.Y, min.Z),
                new(min.X, max.Y, min.Z), new(max.X, max.Y, min.Z),
                new(min.X, min.Y, max.Z), new(max.X, min.Y, max.Z),
                new(min.X, max.Y, max.Z), new(max.X, max.Y, max.Z)
            };

            float minVal = float.PositiveInfinity;
            float maxVal = float.NegativeInfinity;
            foreach (var c in corners)
            {
                var r = Vector3.Transform(new Vector3(c.X * scale.X, c.Y * scale.Y, c.Z * scale.Z), q);
                if (r.Y < minVal) minVal = r.Y;
                if (r.Y > maxVal) maxVal = r.Y;
            }

            minY = float.IsFinite(minVal) ? minVal : 0f;
            return float.IsFinite(maxVal) ? maxVal - minY : 0f;
        }
    }
}
