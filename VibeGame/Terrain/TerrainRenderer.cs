using System.Numerics;
using Raylib_CsLo;
using Serilog;
using Serilog.Core;
using VibeGame.Core;

namespace VibeGame.Terrain
{
    using VibeGame.Biomes;
    // Renderer that draws a simple heightmap mesh and applies basic lighting and biome-like color layers.
    // Now supports sampling a diffuse terrain texture to color triangles.
    public class TerrainRenderer : ITerrainRenderer
    {

        private bool _lowAvailable;
        private bool _highAvailable;

        // GPU textures for proper per-pixel sampling during rendering
        private Texture _lowTex;
        private Texture _highTex;

        // World UV tiling controls how many texture repeats per world unit.
        // Smaller value = larger texels. Example 1 repeat every 2 world units => 0.5f
        private float _lowTiling = 1f / 6f;   // mud/leaves: larger features
        private float _highTiling = 1f / 8f;  // rocks: a bit larger to avoid noise
        private readonly float _textureBlend = 0.62f;  // how strongly textures influence final color
        private readonly float _biomeTintStrength = 0.22f; // 0=no tint, 1=full biome color over texture

        private bool _texturesInitialized;
        
        private readonly ILogger logger = Log.ForContext<TerrainRenderer>();
        private readonly ITextureManager _textureManager;
        private readonly ITerrainTextureRegistry _terrainTextures;
        private string? _lastBiomeIdApplied;
        private Dictionary<string, VibeGame.Biomes.TextureRule>? _currentRules;
        private string? _lowTextureId;
        private string? _highTextureId;
        
        public TerrainRenderer(ITextureManager textureManager, ITerrainTextureRegistry terrainTextures)
        {
            _textureManager = textureManager;
            _terrainTextures = terrainTextures;
            _texturesInitialized = false;
        }

        private void EnsureTextures()
        {
            if (_texturesInitialized) return;
            TryLoadTextures();
            _texturesInitialized = true;
        }

        public void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor)
        {
            EnsureTextures();
            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);
            int half = sizeX / 2;

            Vector3 camPos = camera.position;
            int viewRadiusTiles = 32; // tighter default for performance

            int cx = (int)MathF.Round(camPos.X / tileSize) + half;
            int cz = (int)MathF.Round(camPos.Z / tileSize) + half;
            int xStart = Math.Clamp(cx - viewRadiusTiles, 0, sizeX - 2);
            int xEnd = Math.Clamp(cx + viewRadiusTiles, 0, sizeX - 2);
            int zStart = Math.Clamp(cz - viewRadiusTiles, 0, sizeZ - 2);
            int zEnd = Math.Clamp(cz + viewRadiusTiles, 0, sizeZ - 2);

            Vector3 lightDir = Vector3.Normalize(new Vector3(0.4f, 1.0f, 0.2f));

            for (int z = zStart; z <= zEnd; z++)
            {
                for (int x = xStart; x <= xEnd; x++)
                {
                    float wx0 = (x - half) * tileSize;
                    float wz0 = (z - half) * tileSize;
                    float wx1 = wx0 + tileSize;
                    float wz1 = wz0 + tileSize;

                    // Distance culling to reduce overdraw in far tiles
                    float cxw = (wx0 + wx1) * 0.5f;
                    float czw = (wz0 + wz1) * 0.5f;
                    float dxw = cxw - camPos.X;
                    float dzw = czw - camPos.Z;
                    if ((dxw * dxw + dzw * dzw) > (viewRadiusTiles * tileSize) * (viewRadiusTiles * tileSize))
                        continue;

                    float h00 = heights[x, z];
                    float h10 = heights[x + 1, z];
                    float h01 = heights[x, z + 1];
                    float h11 = heights[x + 1, z + 1];

                    Vector3 p00 = new Vector3(wx0, h00, wz0);
                    Vector3 p10 = new Vector3(wx1, h10, wz0);
                    Vector3 p01 = new Vector3(wx0, h01, wz1);
                    Vector3 p11 = new Vector3(wx1, h11, wz1);

                    if (_lowAvailable || _highAvailable)
                    {
                        // Compute triangle 1 shading and blend
                        Vector3 n1 = Vector3.Normalize(Vector3.Cross(p01 - p00, p10 - p00));
                        float diff1 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n1, lightDir)));
                        // Per-vertex blends
                        float b00 = ComputeBlend(h00, n1);
                        float b01 = ComputeBlend(h01, n1);
                        float b10 = ComputeBlend(h10, n1);
                        // Compute world-space tiled UVs for each corner once per tile
                        Vector2 uv00L = new Vector2(wx0 * _lowTiling, wz0 * _lowTiling);
                        Vector2 uv10L = new Vector2(wx1 * _lowTiling, wz0 * _lowTiling);
                        Vector2 uv11L = new Vector2(wx1 * _lowTiling, wz1 * _lowTiling);
                        Vector2 uv01L = new Vector2(wx0 * _lowTiling, wz1 * _lowTiling);
                        Vector2 uv00H = new Vector2(wx0 * _highTiling, wz0 * _highTiling);
                        Vector2 uv10H = new Vector2(wx1 * _highTiling, wz0 * _highTiling);
                        Vector2 uv11H = new Vector2(wx1 * _highTiling, wz1 * _highTiling);
                        Vector2 uv01H = new Vector2(wx0 * _highTiling, wz1 * _highTiling);

                        // Base pass as two triangles with per-triangle lighting + subtle biome tint
                        Color tintCol1 = Lerp(new Color(255, 255, 255, 255), baseColor, _biomeTintStrength);
                        Color baseLit = new Color(
                            (byte)Math.Clamp((int)(tintCol1.r * diff1), 0, 255),
                            (byte)Math.Clamp((int)(tintCol1.g * diff1), 0, 255),
                            (byte)Math.Clamp((int)(tintCol1.b * diff1), 0, 255),
                            (byte)255);
                        if (_lowAvailable)
                        {
                            DrawTexturedTriangle(_lowTex, p00, p01, p10, uv00L, uv01L, uv10L, baseLit);
                        }
                        else
                        {
                            DrawTexturedTriangle(_highTex, p00, p01, p10, uv00H, uv01H, uv10H, baseLit);
                        }

                        // High pass overlay with per-vertex alpha = blend * strength
                        if (_highAvailable)
                        {
                            float gate1 = HighRuleGate(n1);
                            byte a00 = (byte)MathF.Round(Math.Clamp(b00 * _textureBlend * gate1, 0f, 1f) * 255f);
                            byte a01 = (byte)MathF.Round(Math.Clamp(b01 * _textureBlend * gate1, 0f, 1f) * 255f);
                            byte a10 = (byte)MathF.Round(Math.Clamp(b10 * _textureBlend * gate1, 0f, 1f) * 255f);
                            // We need separate colors per-vertex; RlGl uses one color state, so draw as three single-vertex color changes
                            Raylib.BeginBlendMode(BlendMode.BLEND_ALPHA);
                            RlGl.rlSetTexture(_highTex.id);
                            RlGl.rlBegin(RlGl.RL_TRIANGLES);
                            RlGl.rlColor4ub((byte)(255 * diff1), (byte)(255 * diff1), (byte)(255 * diff1), a00);
                            RlGl.rlTexCoord2f(uv00H.X, 1f - uv00H.Y); RlGl.rlVertex3f(p00.X, p00.Y, p00.Z);
                            RlGl.rlColor4ub((byte)(255 * diff1), (byte)(255 * diff1), (byte)(255 * diff1), a01);
                            RlGl.rlTexCoord2f(uv01H.X, 1f - uv01H.Y); RlGl.rlVertex3f(p01.X, p01.Y, p01.Z);
                            RlGl.rlColor4ub((byte)(255 * diff1), (byte)(255 * diff1), (byte)(255 * diff1), a10);
                            RlGl.rlTexCoord2f(uv10H.X, 1f - uv10H.Y); RlGl.rlVertex3f(p10.X, p10.Y, p10.Z);
                            RlGl.rlEnd();
                            RlGl.rlSetTexture(0);
                            Raylib.EndBlendMode();
                        }

                        // Triangle 2
                        Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                        float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                        float b011 = ComputeBlend(h01, n2);
                        float b111 = ComputeBlend(h11, n2);
                        float b101 = ComputeBlend(h10, n2);
                        Vector2 uv01L2 = uv01L; // wx0,wz1
                        uv11L = new Vector2(wx1 * _lowTiling, wz1 * _lowTiling);
                        Vector2 uv10L2 = uv10L; // wx1,wz0
                        Vector2 uv01H2 = uv01H;
                        uv11H = new Vector2(wx1 * _highTiling, wz1 * _highTiling);
                        Vector2 uv10H2 = uv10H;

                        Color tintCol2 = Lerp(new Color(255, 255, 255, 255), baseColor, _biomeTintStrength);
                        Color baseLit2 = new Color(
                            (byte)Math.Clamp((int)(tintCol2.r * diff2), 0, 255),
                            (byte)Math.Clamp((int)(tintCol2.g * diff2), 0, 255),
                            (byte)Math.Clamp((int)(tintCol2.b * diff2), 0, 255),
                            (byte)255);
                        if (_lowAvailable)
                            DrawTexturedTriangle(_lowTex, p01, p11, p10, uv01L2, uv11L, uv10L2, baseLit2);
                        else
                            DrawTexturedTriangle(_highTex, p01, p11, p10, uv01H2, uv11H, uv10H2, baseLit2);

                        if (_highAvailable)
                        {
                            float gate2 = HighRuleGate(n2);
                            byte a01b = (byte)MathF.Round(Math.Clamp(b011 * _textureBlend * gate2, 0f, 1f) * 255f);
                            byte a11b = (byte)MathF.Round(Math.Clamp(b111 * _textureBlend * gate2, 0f, 1f) * 255f);
                            byte a10b = (byte)MathF.Round(Math.Clamp(b101 * _textureBlend * gate2, 0f, 1f) * 255f);
                            Raylib.BeginBlendMode(BlendMode.BLEND_ALPHA);
                            RlGl.rlSetTexture(_highTex.id);
                            RlGl.rlBegin(RlGl.RL_TRIANGLES);
                            RlGl.rlColor4ub((byte)(255 * diff2), (byte)(255 * diff2), (byte)(255 * diff2), a01b);
                            RlGl.rlTexCoord2f(uv01H2.X, 1f - uv01H2.Y); RlGl.rlVertex3f(p01.X, p01.Y, p01.Z);
                            RlGl.rlColor4ub((byte)(255 * diff2), (byte)(255 * diff2), (byte)(255 * diff2), a11b);
                            RlGl.rlTexCoord2f(uv11H.X, 1f - uv11H.Y); RlGl.rlVertex3f(p11.X, p11.Y, p11.Z);
                            RlGl.rlColor4ub((byte)(255 * diff2), (byte)(255 * diff2), (byte)(255 * diff2), a10b);
                            RlGl.rlTexCoord2f(uv10H2.X, 1f - uv10H2.Y); RlGl.rlVertex3f(p10.X, p10.Y, p10.Z);
                            RlGl.rlEnd();
                            RlGl.rlSetTexture(0);
                            Raylib.EndBlendMode();
                        }
                    }
                }
            }
        }

        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera, Color baseColor)
        {
            EnsureTextures();
            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);

            Vector3 camPos = camera.position;
            int viewRadiusTiles = 28; // slightly reduced per-chunk tile radius for perf

            // Compute camera index in this chunk's tile space
            float localCamX = (camPos.X - originWorld.X) / tileSize;
            float localCamZ = (camPos.Z - originWorld.Y) / tileSize;
            int cx = (int)MathF.Round(localCamX);
            int cz = (int)MathF.Round(localCamZ);
            int xStart = Math.Clamp(cx - viewRadiusTiles, 0, sizeX - 2);
            int xEnd = Math.Clamp(cx + viewRadiusTiles, 0, sizeX - 2);
            int zStart = Math.Clamp(cz - viewRadiusTiles, 0, sizeZ - 2);
            int zEnd = Math.Clamp(cz + viewRadiusTiles, 0, sizeZ - 2);

            Vector3 lightDir = Vector3.Normalize(new Vector3(0.4f, 1.0f, 0.2f));

            // Choose base texture and tiling
            Texture baseTex = _lowAvailable ? _lowTex : _highTex;
            float tiling = _lowAvailable ? _lowTiling : _highTiling;
            if (baseTex.id == 0) return;

            // Batch all triangles for this chunk to drastically reduce draw calls
            RlGl.rlSetTexture(baseTex.id);
            RlGl.rlDisableBackfaceCulling();
            RlGl.rlBegin(RlGl.RL_TRIANGLES);

            float nearDist = 14f * tileSize;
            float midDist = 22f * tileSize;
            float farDist = 30f * tileSize;
            float far2 = farDist * farDist;
            float mid2 = midDist * midDist;

            // Clamp camera X into this chunk span to estimate row distance
            float chunkX0 = originWorld.X;
            float chunkX1 = originWorld.X + (sizeX - 1) * tileSize;
            float clampedCamX = Math.Clamp(camPos.X, chunkX0, chunkX1);

            for (int z = zStart; z <= zEnd; )
            {
                // Determine a row step based on distance from camera to this row
                float wz0Row = originWorld.Y + z * tileSize;
                float wz1Row = wz0Row + tileSize;
                float cxwRow = clampedCamX; // use camera X within chunk
                float czwRow = (wz0Row + wz1Row) * 0.5f;
                float dxRow = cxwRow - camPos.X;
                float dzRow = czwRow - camPos.Z;
                float d2Row = dxRow * dxRow + dzRow * dzRow;
                int rowStep = d2Row > far2 ? 4 : (d2Row > mid2 ? 2 : 1);
                if (rowStep < 1) rowStep = 1;

                for (int x = xStart; x <= xEnd; )
                {
                    int step = rowStep;
                    int x2i = Math.Min(x + step, sizeX - 1);
                    int z2i = Math.Min(z + step, sizeZ - 1);
                    if (x2i == x) x2i = x + 1;
                    if (z2i == z) z2i = z + 1;

                    float wx0 = originWorld.X + x * tileSize;
                    float wz0 = originWorld.Y + z * tileSize;
                    float wx1 = originWorld.X + x2i * tileSize;
                    float wz1 = originWorld.Y + z2i * tileSize;

                    // Distance culling for chunked rendering path
                    float cxw = (wx0 + wx1) * 0.5f;
                    float czw = (wz0 + wz1) * 0.5f;
                    float dxw = cxw - camPos.X;
                    float dzw = czw - camPos.Z;
                    if ((dxw * dxw + dzw * dzw) > (viewRadiusTiles * tileSize) * (viewRadiusTiles * tileSize))
                    {
                        x = Math.Max(x + 1, x2i);
                        continue;
                    }

                    float h00 = heights[x, z];
                    float h10 = heights[x2i, z];
                    float h01 = heights[x, z2i];
                    float h11 = heights[x2i, z2i];

                    Vector3 p00 = new Vector3(wx0, h00, wz0);
                    Vector3 p10 = new Vector3(wx1, h10, wz0);
                    Vector3 p01 = new Vector3(wx0, h01, wz1);
                    Vector3 p11 = new Vector3(wx1, h11, wz1);

                    // Triangle 1
                    Vector3 n1 = Vector3.Normalize(Vector3.Cross(p01 - p00, p10 - p00));
                    float diff1 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n1, lightDir)));
                    Color tint1 = Lerp(new Color(255, 255, 255, 255), baseColor, _biomeTintStrength);
                    byte r1 = (byte)Math.Clamp((int)(tint1.r * diff1), 0, 255);
                    byte g1 = (byte)Math.Clamp((int)(tint1.g * diff1), 0, 255);
                    byte b1 = (byte)Math.Clamp((int)(tint1.b * diff1), 0, 255);

                    Vector2 uv00 = new Vector2(wx0 * tiling, wz0 * tiling);
                    Vector2 uv01 = new Vector2(wx0 * tiling, wz1 * tiling);
                    Vector2 uv10 = new Vector2(wx1 * tiling, wz0 * tiling);

                    RlGl.rlColor4ub(r1, g1, b1, 255);
                    RlGl.rlTexCoord2f(uv00.X, 1f - uv00.Y); RlGl.rlVertex3f(p00.X, p00.Y, p00.Z);
                    RlGl.rlTexCoord2f(uv01.X, 1f - uv01.Y); RlGl.rlVertex3f(p01.X, p01.Y, p01.Z);
                    RlGl.rlTexCoord2f(uv10.X, 1f - uv10.Y); RlGl.rlVertex3f(p10.X, p10.Y, p10.Z);

                    // Triangle 2
                    Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                    float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                    Color tint2 = Lerp(new Color(255, 255, 255, 255), baseColor, _biomeTintStrength);
                    byte r2 = (byte)Math.Clamp((int)(tint2.r * diff2), 0, 255);
                    byte g2 = (byte)Math.Clamp((int)(tint2.g * diff2), 0, 255);
                    byte b2 = (byte)Math.Clamp((int)(tint2.b * diff2), 0, 255);

                    Vector2 uv11 = new Vector2(wx1 * tiling, wz1 * tiling);

                    RlGl.rlColor4ub(r2, g2, b2, 255);
                    RlGl.rlTexCoord2f(uv01.X, 1f - uv01.Y); RlGl.rlVertex3f(p01.X, p01.Y, p01.Z);
                    RlGl.rlTexCoord2f(uv11.X, 1f - uv11.Y); RlGl.rlVertex3f(p11.X, p11.Y, p11.Z);
                    RlGl.rlTexCoord2f(uv10.X, 1f - uv10.Y); RlGl.rlVertex3f(p10.X, p10.Y, p10.Z);

                    x = Math.Max(x + 1, x2i);
                }
                z = Math.Max(z + 1, z + rowStep);
            }

            RlGl.rlEnd();
            RlGl.rlEnableBackfaceCulling();
            RlGl.rlSetTexture(0);
        }

        private void TryLoadTextures()
        {
            // With config-driven biome textures, availability is determined by what ApplyBiomeTextures has set.
            _lowAvailable = _lowTex.id != 0;
            _highAvailable = _highTex.id != 0;

            logger.Debug("[Terrain] Texture availability -> low: {Low}, high: {High}", _lowAvailable, _highAvailable);
            // No exception here; in chunked rendering, ApplyBiomeTextures is called before RenderAt.
            // If nothing is available yet, rendering will be skipped until a biome applies textures.
        }


        private static void DrawTexturedTriangle(Texture tex, Vector3 a, Vector3 b, Vector3 c,
            Vector2 uva, Vector2 uvb, Vector2 uvc, Color color)
        {
            if (tex.id == 0) return;
            float vA = 1f - uva.Y;
            float vB = 1f - uvb.Y;
            float vC = 1f - uvc.Y;
            // Use rlSetTexture for proper batching with raylib default 3D mode
            RlGl.rlSetTexture(tex.id);
            RlGl.rlDisableBackfaceCulling();
            RlGl.rlBegin(RlGl.RL_TRIANGLES);
            RlGl.rlColor4ub(color.r, color.g, color.b, color.a);
            RlGl.rlTexCoord2f(uva.X, vA); RlGl.rlVertex3f(a.X, a.Y, a.Z);
            RlGl.rlTexCoord2f(uvb.X, vB); RlGl.rlVertex3f(b.X, b.Y, b.Z);
            RlGl.rlTexCoord2f(uvc.X, vC); RlGl.rlVertex3f(c.X, c.Y, c.Z);
            RlGl.rlEnd();
            RlGl.rlEnableBackfaceCulling();
            RlGl.rlSetTexture(0);
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






        // Gate the high/overlay texture based on biome TextureRules for the high texture id
        private float HighRuleGate(Vector3 normal)
        {
            if (string.IsNullOrWhiteSpace(_highTextureId) || _currentRules == null)
                return 1f;
            if (!_currentRules.TryGetValue(_highTextureId, out var rule) || rule == null)
                return 1f;

            // Compute slope angle in degrees from surface normal
            float upDot = MathF.Max(0f, Vector3.Dot(Vector3.UnitY, Vector3.Normalize(normal)));
            upDot = Math.Clamp(upDot, -1f, 1f);
            float slopeDeg = MathF.Acos(upDot) * (180f / MathF.PI);

            float gate = 1f;
            bool hasMin = rule.SlopeMin.HasValue;
            bool hasMax = rule.SlopeMax.HasValue;
            if (hasMin && hasMax)
            {
                gate = Smoothstep(rule.SlopeMin!.Value, rule.SlopeMax!.Value, slopeDeg);
            }
            else if (hasMin)
            {
                gate = slopeDeg >= rule.SlopeMin!.Value ? 1f : 0f;
            }
            else if (hasMax)
            {
                gate = slopeDeg <= rule.SlopeMax!.Value ? 1f : 0f;
            }

            // Altitude rules are ignored in this overlay gate because we do not
            // have per-vertex world height here; the terrain blend already encodes height.
            return Math.Clamp(gate, 0f, 1f);
        }


        private static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            byte r = (byte)MathF.Round(a.r + (b.r - a.r) * t);
            byte g = (byte)MathF.Round(a.g + (b.g - a.g) * t);
            byte bch = (byte)MathF.Round(a.b + (b.b - a.b) * t);
            byte aCh = (byte)MathF.Round(a.a + (b.a - a.a) * t);
            return new Color(r, g, bch, aCh);
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
                float lt = _lowTiling, ht = _highTiling;

                var layers = biome.SurfaceTextures ?? new List<SurfaceTextureLayer>();
                _lowTextureId = null;
                _highTextureId = null;

                if (layers.Count > 0)
                {
                    var id0 = layers[0].TextureId;
                    _lowTextureId = id0;
                    var p0 = _terrainTextures.GetResolvedAlbedoPath(id0);
                    if (!string.IsNullOrWhiteSpace(p0) && _textureManager.TryGetOrLoadByPath(p0!, out low) && low.id != 0)
                    {
                        lowOk = true;
                        lt = 1f / MathF.Max(0.001f, _terrainTextures.GetTileSizeOrDefault(id0, 6f));
                    }
                }
                if (layers.Count > 1)
                {
                    var id1 = layers[1].TextureId;
                    _highTextureId = id1;
                    var p1 = _terrainTextures.GetResolvedAlbedoPath(id1);
                    if (!string.IsNullOrWhiteSpace(p1) && _textureManager.TryGetOrLoadByPath(p1!, out high) && high.id != 0)
                    {
                        highOk = true;
                        ht = 1f / MathF.Max(0.001f, _terrainTextures.GetTileSizeOrDefault(id1, 8f));
                    }
                }

                if (lowOk)
                {
                    _lowTex = low;
                    _lowAvailable = true;
                    _lowTiling = lt;
                }
                if (highOk)
                {
                    _highTex = high;
                    _highAvailable = true;
                    _highTiling = ht;
                }
                else
                {
                    _highAvailable = false;
                }

                // Cache rules map for gating of high layer
                _currentRules = biome.TextureRules != null && biome.TextureRules.Count > 0 ? new Dictionary<string, VibeGame.Biomes.TextureRule>(biome.TextureRules, StringComparer.OrdinalIgnoreCase) : null;

                _lastBiomeIdApplied = biome.Id;
                logger.Debug("[Terrain] Applied biome textures for {BiomeId}: lowOk={Low} highOk={High}", biome.Id, lowOk, highOk);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Failed to apply biome textures for {BiomeId}", biome.Id);
            }
        }

    }
}
