using System.Numerics;
using Raylib_CsLo;
using Serilog;
using VibeGame.Biomes;
using VibeGame.Core;

namespace VibeGame.Terrain
{
    public class TerrainRenderer : ITerrainRenderer
    {
        private bool _lowAvailable;
        private bool _highAvailable;
        private Texture _lowTex;
        private Texture _highTex;

        private float _lowTiling = 0.05f;
        private float _highTiling = 0.07f;
        private readonly float _biomeTintStrength = 0.15f;

        private Color _tint = Raylib.WHITE;

        private bool _texturesInitialized;

        private readonly ILogger logger = Log.ForContext<TerrainRenderer>();
        private readonly ITextureManager _textureManager;
        private readonly ITerrainTextureRegistry _terrainTextures;
        private readonly IBiomeProvider _biomeProvider;
        private string? _lastBiomeIdApplied;
        private Dictionary<string, TextureRule>? _currentRules;
        private string? _lowTextureId;
        private string? _highTextureId;

        public TerrainRenderer(ITextureManager textureManager, ITerrainTextureRegistry terrainTextures, IBiomeProvider biomeProvider)
        {
            _textureManager = textureManager;
            _terrainTextures = terrainTextures;
            _biomeProvider = biomeProvider;
        }

        private void EnsureTextures()
        {
            if (_texturesInitialized)
                return;

            TryLoadTextures();
            _texturesInitialized = true;
        }

        public void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor)
        {
            // Not used, see RenderAt
        }

        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera)
        {
            EnsureTextures();

            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);
            Vector3 camPos = camera.position;
            int viewRadiusTiles = 28;

            float localCamX = (camPos.X - originWorld.X) / tileSize;
            float localCamZ = (camPos.Z - originWorld.Y) / tileSize;
            int cx = (int)MathF.Round(localCamX);
            int cz = (int)MathF.Round(localCamZ);

            int xStart = Math.Clamp(cx - viewRadiusTiles, 0, sizeX - 2);
            int xEnd = Math.Clamp(cx + viewRadiusTiles, 0, sizeX - 2);
            int zStart = Math.Clamp(cz - viewRadiusTiles, 0, sizeZ - 2);
            int zEnd = Math.Clamp(cz + viewRadiusTiles, 0, sizeZ - 2);

            RlGl.rlDisableBackfaceCulling();
            RlGl.rlBegin(RlGl.RL_TRIANGLES);

            float farDist = 30f * tileSize;
            float far2 = farDist * farDist;

            for (int z = zStart; z <= zEnd; z++)
            {
                for (int x = xStart; x <= xEnd; x++)
                {
                    float wx0 = originWorld.X + x * tileSize;
                    float wz0 = originWorld.Y + z * tileSize;
                    float wx1 = wx0 + tileSize;
                    float wz1 = wz0 + tileSize;

                    float dx = (wx0 + wx1) * 0.5f - camPos.X;
                    float dz = (wz0 + wz1) * 0.5f - camPos.Z;
                    if ((dx * dx + dz * dz) > far2) continue;

                    float h00 = heights[x, z];
                    float h10 = heights[x + 1, z];
                    float h01 = heights[x, z + 1];
                    float h11 = heights[x + 1, z + 1];

                    Vector3 p00 = new Vector3(wx0, h00, wz0);
                    Vector3 p10 = new Vector3(wx1, h10, wz0);
                    Vector3 p01 = new Vector3(wx0, h01, wz1);
                    Vector3 p11 = new Vector3(wx1, h11, wz1);

                    Vector3 n1 = Vector3.Normalize(Vector3.Cross(p01 - p00, p10 - p00));
                    Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));

                    Color b00 = GetBiomeTint(wx0, wz0);
                    Color b10 = GetBiomeTint(wx1, wz0);
                    Color b01 = GetBiomeTint(wx0, wz1);
                    Color b11 = GetBiomeTint(wx1, wz1);

                    // Continuous UVs
                    float u0 = x * _lowTiling;
                    float v0 = z * _lowTiling;
                    float u1 = (x + 1) * _lowTiling;
                    float v1 = (z + 1) * _lowTiling;

                    // --- Low layer ---
                    if (_lowAvailable && _lowTex.id != 0)
                    {
                        DrawTexturedTriangleLayer(_lowTex, p00, p01, p10, b00, b01, b10, n1, u0, v0, u1, v1, 1f);
                        DrawTexturedTriangleLayer(_lowTex, p01, p11, p10, b01, b11, b10, n2, u0, v0, u1, v1, 1f);
                    }

                    // --- High layer ---
                    if (_highAvailable && _highTex.id != 0)
                    {
                        float blend1 = ComputeBlend(h00, n1) * HighRuleGate(n1);
                        float blend2 = ComputeBlend(h01, n2) * HighRuleGate(n2);
                        DrawTexturedTriangleLayer(_highTex, p00, p01, p10, b00, b01, b10, n1, u0, v0, u1, v1, blend1);
                        DrawTexturedTriangleLayer(_highTex, p01, p11, p10, b01, b11, b10, n2, u0, v0, u1, v1, blend2);
                    }
                }
            }

            RlGl.rlEnd();
            RlGl.rlEnableBackfaceCulling();
            RlGl.rlSetTexture(0);
        }

        private void DrawTexturedTriangleLayer(
            Texture tex,
            Vector3 p0, Vector3 p1, Vector3 p2,
            Color b0, Color b1, Color b2,
            Vector3 normal,
            float u0, float v0, float u1, float v1,
            float textureBlend = 1f)
        {
            if (tex.id == 0) return;

            Vector3 lightDir = Vector3.Normalize(new Vector3(0.4f, 1f, 0.2f));
            float diff = MathF.Max(0.35f, MathF.Min(1f, Vector3.Dot(Vector3.Normalize(normal), lightDir)));

            RlGl.rlSetTexture(tex.id);

            // Compute per-vertex UVs from tile corners
            Vector2 uv0 = new(u0, v0);
            Vector2 uv1 = new(u0, v1);
            Vector2 uv2 = new(u1, v0);

            Color c0 = ApplyLightingAndTint(b0, diff, _biomeTintStrength, textureBlend);
            Color c1 = ApplyLightingAndTint(b1, diff, _biomeTintStrength, textureBlend);
            Color c2 = ApplyLightingAndTint(b2, diff, _biomeTintStrength, textureBlend);

            RlGl.rlColor4ub(c0.r, c0.g, c0.b, c0.a);
            RlGl.rlTexCoord2f(uv0.X, 1f - uv0.Y);
            RlGl.rlVertex3f(p0.X, p0.Y, p0.Z);

            RlGl.rlColor4ub(c1.r, c1.g, c1.b, c1.a);
            RlGl.rlTexCoord2f(uv1.X, 1f - uv1.Y);
            RlGl.rlVertex3f(p1.X, p1.Y, p1.Z);

            RlGl.rlColor4ub(c2.r, c2.g, c2.b, c2.a);
            RlGl.rlTexCoord2f(uv2.X, 1f - uv2.Y);
            RlGl.rlVertex3f(p2.X, p2.Y, p2.Z);
        }

        private static Color ApplyLightingAndTint(Color biomeColor, float diff, float tintStrength, float blend)
        {
            float intensity = diff * blend;
            byte r = (byte)Math.Clamp(biomeColor.r * intensity, 0, 255);
            byte g = (byte)Math.Clamp(biomeColor.g * intensity, 0, 255);
            byte b = (byte)Math.Clamp(biomeColor.b * intensity, 0, 255);
            return new Color(r, g, b, biomeColor.a);
        }

        private Color GetBiomeTint(float worldX, float worldZ)
        {
            var biome = _biomeProvider?.GetBiomeAt(new Vector2(worldX, worldZ), null);
            if (biome == null)
                return new Color(255, 255, 255, 255);

            if (_lastBiomeIdApplied != biome.Data.Id)
                ApplyBiomeTextures(biome.Data);

            return ToRaylibColor(biome.Data.Color);
        }

        public static Color ToRaylibColor(System.Drawing.Color c)
            => new Color(c.R, c.G, c.B, c.A);

        private void TryLoadTextures()
        {
            _lowAvailable = _lowTex.id != 0;
            _highAvailable = _highTex.id != 0;
        }

        private static float Smoothstep(float edge0, float edge1, float x)
        {
            float t = Math.Clamp((x - edge0) / MathF.Max(0.0001f, edge1 - edge0), 0f, 1f);
            return t * t * (3f - 2f * t);
        }

        private static float ComputeBlend(float height, Vector3 normal)
        {
            float ht = Smoothstep(3.2f, 4.2f, height);
            float upDot = MathF.Max(0f, Vector3.Dot(Vector3.UnitY, Vector3.Normalize(normal)));
            float slope = 1f - upDot;
            float rockBoost = Math.Clamp((slope - 0.35f) / 0.35f, 0f, 1f);
            ht = Math.Clamp(ht + rockBoost * 0.5f, 0f, 1f);
            return ht;
        }

        private float HighRuleGate(Vector3 normal)
        {
            if (string.IsNullOrWhiteSpace(_highTextureId) || _currentRules == null)
                return 1f;
            if (!_currentRules.TryGetValue(_highTextureId, out var rule) || rule == null)
                return 1f;

            float upDot = MathF.Max(0f, Vector3.Dot(Vector3.UnitY, Vector3.Normalize(normal)));
            float slopeDeg = MathF.Acos(upDot) * (180f / MathF.PI);
            float gate = 1f;

            if (rule.SlopeMin.HasValue && rule.SlopeMax.HasValue)
                gate = Smoothstep(rule.SlopeMin.Value, rule.SlopeMax.Value, slopeDeg);
            else if (rule.SlopeMin.HasValue)
                gate = slopeDeg >= rule.SlopeMin.Value ? 1f : 0f;
            else if (rule.SlopeMax.HasValue)
                gate = slopeDeg <= rule.SlopeMax.Value ? 1f : 0f;

            return Math.Clamp(gate, 0f, 1f);
        }

        public void ApplyBiomeTextures(BiomeData biome)
        {
            if (biome == null) return;
            if (!string.IsNullOrEmpty(_lastBiomeIdApplied) && string.Equals(_lastBiomeIdApplied, biome.Id, StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                Texture low = default;
                Texture high = default;
                bool lowOk = false, highOk = false;

                var layers = biome.SurfaceTextures ?? new List<SurfaceTextureLayer>();
                _lowTextureId = null;
                _highTextureId = null;

                if (layers.Count > 0)
                {
                    var id0 = layers[0].TextureId;
                    _lowTextureId = id0;
                    var p0 = _terrainTextures.GetResolvedAlbedoPath(id0);
                    if (!string.IsNullOrWhiteSpace(p0) && _textureManager.TryGetOrLoadByPath(p0!, out low) && low.id != 0)
                        lowOk = true;
                }

                if (layers.Count > 1)
                {
                    var id1 = layers[1].TextureId;
                    _highTextureId = id1;
                    var p1 = _terrainTextures.GetResolvedAlbedoPath(id1);
                    if (!string.IsNullOrWhiteSpace(p1) && _textureManager.TryGetOrLoadByPath(p1!, out high) && high.id != 0)
                        highOk = true;
                }

                _lowTex = low;
                _highTex = high;
                _lowAvailable = lowOk;
                _highAvailable = highOk;

                _lastBiomeIdApplied = biome.Id;
                _currentRules = biome.TextureRules;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "[Terrain] Failed to apply biome textures.");
            }
        }

        public void SetColorTint(Color color)
        {
            _tint = color;
        }
    }
}
