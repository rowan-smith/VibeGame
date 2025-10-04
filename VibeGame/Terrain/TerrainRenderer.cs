using System.Numerics;
using Raylib_CsLo;

namespace VibeGame.Terrain
{
    // Renderer that draws a simple heightmap mesh and applies basic lighting and biome-like color layers.
    // Now supports sampling a diffuse terrain texture to color triangles.
    public class TerrainRenderer : ITerrainRenderer
    {
        // Terrain diffuse textures (low = mud/leaves, high = aerial rocks)
        private readonly string _lowDiffusePath = Path.Combine("assets", "terrain", "brown_mud_leaves", "textures", "brown_mud_leaves_01_diff_4k.jpg");
        private readonly string _highDiffusePath = Path.Combine("assets", "terrain", "aerial_rocks", "textures", "aerial_rocks_04_diff_4k.jpg");

        private Image _lowImage;
        private Image _highImage;
        private bool _lowAvailable;
        private bool _highAvailable;
        private int _lowW, _lowH;
        private int _highW, _highH;

        // GPU textures for proper per-pixel sampling during rendering
        private Texture _lowTex;
        private Texture _highTex;

        // World UV tiling controls how many texture repeats per world unit.
        // Smaller value = larger texels. Example 1 repeat every 2 world units => 0.5f
        private readonly float _lowTiling = 1f / 6f;   // mud/leaves: larger features
        private readonly float _highTiling = 1f / 8f;  // rocks: a bit larger to avoid noise
        private readonly float _textureBlend = 0.62f;  // how strongly textures influence final color

        public TerrainRenderer()
        {
            TryLoadTextures();
        }

        public void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor)
        {
            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);
            int half = sizeX / 2;

            Vector3 camPos = camera.position;
            int viewRadiusTiles = 50; // reasonable default

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
                        // UVs for low texture
                        Vector2 uv00L = new Vector2(Frac(wx0 * _lowTiling), Frac(wz0 * _lowTiling));
                        Vector2 uv01L = new Vector2(Frac(wx0 * _lowTiling), Frac(wz1 * _lowTiling));
                        Vector2 uv10L = new Vector2(Frac(wx1 * _lowTiling), Frac(wz0 * _lowTiling));
                        // UVs for high texture
                        Vector2 uv00H = new Vector2(Frac(wx0 * _highTiling), Frac(wz0 * _highTiling));
                        Vector2 uv01H = new Vector2(Frac(wx0 * _highTiling), Frac(wz1 * _highTiling));
                        Vector2 uv10H = new Vector2(Frac(wx1 * _highTiling), Frac(wz0 * _highTiling));

                        // Low pass opaque with lighting
                        Color baseLit = new Color((byte)(255 * diff1), (byte)(255 * diff1), (byte)(255 * diff1), (byte)255);
                        if (_lowAvailable)
                            DrawTexturedTriangle(_lowTex, p00, p01, p10, uv00L, uv01L, uv10L, baseLit);
                        else
                            DrawTexturedTriangle(_highTex, p00, p01, p10, uv00H, uv01H, uv10H, baseLit);

                        // High pass overlay with per-vertex alpha = blend * strength
                        if (_highAvailable)
                        {
                            byte a00 = (byte)MathF.Round(Math.Clamp(b00 * _textureBlend, 0f, 1f) * 255f);
                            byte a01 = (byte)MathF.Round(Math.Clamp(b01 * _textureBlend, 0f, 1f) * 255f);
                            byte a10 = (byte)MathF.Round(Math.Clamp(b10 * _textureBlend, 0f, 1f) * 255f);
                            // We need separate colors per-vertex; RlGl uses one color state, so draw as three single-vertex color changes
                            RlGl.rlSetTexture(_highTex.id);
                            RlGl.rlBegin(RlGl.RL_TRIANGLES);
                            RlGl.rlColor4ub((byte)(255 * diff1), (byte)(255 * diff1), (byte)(255 * diff1), a00);
                            RlGl.rlTexCoord2f(uv00H.X, uv00H.Y); RlGl.rlVertex3f(p00.X, p00.Y, p00.Z);
                            RlGl.rlColor4ub((byte)(255 * diff1), (byte)(255 * diff1), (byte)(255 * diff1), a01);
                            RlGl.rlTexCoord2f(uv01H.X, uv01H.Y); RlGl.rlVertex3f(p01.X, p01.Y, p01.Z);
                            RlGl.rlColor4ub((byte)(255 * diff1), (byte)(255 * diff1), (byte)(255 * diff1), a10);
                            RlGl.rlTexCoord2f(uv10H.X, uv10H.Y); RlGl.rlVertex3f(p10.X, p10.Y, p10.Z);
                            RlGl.rlEnd();
                            RlGl.rlSetTexture(0);
                        }

                        // Triangle 2
                        Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                        float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                        float b011 = ComputeBlend(h01, n2);
                        float b111 = ComputeBlend(h11, n2);
                        float b101 = ComputeBlend(h10, n2);
                        Vector2 uv01L2 = uv01L; // wx0,wz1
                        Vector2 uv11L = new Vector2(Frac(wx1 * _lowTiling), Frac(wz1 * _lowTiling));
                        Vector2 uv10L2 = uv10L; // wx1,wz0
                        Vector2 uv01H2 = uv01H;
                        Vector2 uv11H = new Vector2(Frac(wx1 * _highTiling), Frac(wz1 * _highTiling));
                        Vector2 uv10H2 = uv10H;

                        Color baseLit2 = new Color((byte)(255 * diff2), (byte)(255 * diff2), (byte)(255 * diff2), (byte)255);
                        if (_lowAvailable)
                            DrawTexturedTriangle(_lowTex, p01, p11, p10, uv01L2, uv11L, uv10L2, baseLit2);
                        else
                            DrawTexturedTriangle(_highTex, p01, p11, p10, uv01H2, uv11H, uv10H2, baseLit2);

                        if (_highAvailable)
                        {
                            byte a01b = (byte)MathF.Round(Math.Clamp(b011 * _textureBlend, 0f, 1f) * 255f);
                            byte a11b = (byte)MathF.Round(Math.Clamp(b111 * _textureBlend, 0f, 1f) * 255f);
                            byte a10b = (byte)MathF.Round(Math.Clamp(b101 * _textureBlend, 0f, 1f) * 255f);
                            RlGl.rlSetTexture(_highTex.id);
                            RlGl.rlBegin(RlGl.RL_TRIANGLES);
                            RlGl.rlColor4ub((byte)(255 * diff2), (byte)(255 * diff2), (byte)(255 * diff2), a01b);
                            RlGl.rlTexCoord2f(uv01H2.X, uv01H2.Y); RlGl.rlVertex3f(p01.X, p01.Y, p01.Z);
                            RlGl.rlColor4ub((byte)(255 * diff2), (byte)(255 * diff2), (byte)(255 * diff2), a11b);
                            RlGl.rlTexCoord2f(uv11H.X, uv11H.Y); RlGl.rlVertex3f(p11.X, p11.Y, p11.Z);
                            RlGl.rlColor4ub((byte)(255 * diff2), (byte)(255 * diff2), (byte)(255 * diff2), a10b);
                            RlGl.rlTexCoord2f(uv10H2.X, uv10H2.Y); RlGl.rlVertex3f(p10.X, p10.Y, p10.Z);
                            RlGl.rlEnd();
                            RlGl.rlSetTexture(0);
                        }
                    }
                    else
                    {
                        // Fallback to previous flat-colored rendering
                        Vector3 n1 = Vector3.Normalize(Vector3.Cross(p01 - p00, p10 - p00));
                        float diff1 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n1, lightDir)));
                        Color triCol1 = GetTriangleColor(wx0, wz0, wx0, wz1, wx1, wz0, (h00 + h01 + h10) / 3f, n1);
                        Color c1 = Tint(triCol1, diff1);
                        Raylib.DrawTriangle3D(p00, p01, p10, c1);

                        Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                        float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                        Color triCol2 = GetTriangleColor(wx0, wz1, wx1, wz1, wx1, wz0, (h01 + h11 + h10) / 3f, n2);
                        Color c2 = Tint(triCol2, diff2);
                        Raylib.DrawTriangle3D(p01, p11, p10, c2);
                    }
                }
            }
        }

        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera, Color baseColor)
        {
            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);

            Vector3 camPos = camera.position;
            int viewRadiusTiles = 50;

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

            for (int z = zStart; z <= zEnd; z++)
            {
                for (int x = xStart; x <= xEnd; x++)
                {
                    float wx0 = originWorld.X + x * tileSize;
                    float wz0 = originWorld.Y + z * tileSize;
                    float wx1 = wx0 + tileSize;
                    float wz1 = wz0 + tileSize;

                    float h00 = heights[x, z];
                    float h10 = heights[x + 1, z];
                    float h01 = heights[x, z + 1];
                    float h11 = heights[x + 1, z + 1];

                    Vector3 p00 = new Vector3(wx0, h00, wz0);
                    Vector3 p10 = new Vector3(wx1, h10, wz0);
                    Vector3 p01 = new Vector3(wx0, h01, wz1);
                    Vector3 p11 = new Vector3(wx1, h11, wz1);

                    Vector3 n1 = Vector3.Normalize(Vector3.Cross(p01 - p00, p10 - p00));
                    float diff1 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n1, lightDir)));
                    Color triCol1 = GetTriangleColor(wx0, wz0, wx0, wz1, wx1, wz0, (h00 + h01 + h10) / 3f, n1);
                    Color c1 = Tint(triCol1, diff1);
                    Raylib.DrawTriangle3D(p00, p01, p10, c1);

                    Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                    float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                    Color triCol2 = GetTriangleColor(wx0, wz1, wx1, wz1, wx1, wz0, (h01 + h11 + h10) / 3f, n2);
                    Color c2 = Tint(triCol2, diff2);
                    Raylib.DrawTriangle3D(p01, p11, p10, c2);
                }
            }
        }

        private void TryLoadTextures()
        {
            try
            {
                string Resolve(string rel)
                {
                    string wd = rel;
                    string ex = Path.Combine(AppContext.BaseDirectory ?? string.Empty, rel);
                    return File.Exists(wd) ? wd : (File.Exists(ex) ? ex : rel);
                }

                string lowPath = Resolve(_lowDiffusePath);
                if (File.Exists(lowPath))
                {
                    _lowImage = Raylib.LoadImage(lowPath);
                    _lowTex = Raylib.LoadTextureFromImage(_lowImage);
                    _lowW = _lowImage.width;
                    _lowH = _lowImage.height;
                    _lowAvailable = _lowW > 0 && _lowH > 0 && _lowTex.id != 0;
                }
                else _lowAvailable = false;

                string highPath = Resolve(_highDiffusePath);
                if (File.Exists(highPath))
                {
                    _highImage = Raylib.LoadImage(highPath);
                    _highTex = Raylib.LoadTextureFromImage(_highImage);
                    _highW = _highImage.width;
                    _highH = _highImage.height;
                    _highAvailable = _highW > 0 && _highH > 0 && _highTex.id != 0;
                }
                else _highAvailable = false;
            }
            catch
            {
                _lowAvailable = false;
                _highAvailable = false;
            }
        }

        private static float Frac(float v) => v - MathF.Floor(v);

        private static void DrawTexturedTriangle(Texture tex, Vector3 a, Vector3 b, Vector3 c,
            Vector2 uva, Vector2 uvb, Vector2 uvc, Color color)
        {
            if (tex.id == 0) return;
            RlGl.rlSetTexture(tex.id);
            RlGl.rlBegin(RlGl.RL_TRIANGLES);
            RlGl.rlColor4ub(color.r, color.g, color.b, color.a);
            RlGl.rlTexCoord2f(uva.X, uva.Y); RlGl.rlVertex3f(a.X, a.Y, a.Z);
            RlGl.rlTexCoord2f(uvb.X, uvb.Y); RlGl.rlVertex3f(b.X, b.Y, b.Z);
            RlGl.rlTexCoord2f(uvc.X, uvc.Y); RlGl.rlVertex3f(c.X, c.Y, c.Z);
            RlGl.rlEnd();
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

        private Color GetTriangleColor(float wxA, float wzA, float wxB, float wzB, float wxC, float wzC, float heightAvg, Vector3 normal)
        {
            Color biome = SampleLayerColor(heightAvg, normal);

            if (!_lowAvailable && !_highAvailable)
                return biome;

            // Triangle centroid
            float cx = (wxA + wxB + wxC) / 3f;
            float cz = (wzA + wzB + wzC) / 3f;

            // Height-based blend between low and high textures with smoothstep
            float lowMax = 3.2f;   // below this fully low texture
            float highMin = 4.2f;  // above this fully high texture
            float ht = Math.Clamp((heightAvg - lowMax) / Math.Max(0.0001f, highMin - lowMax), 0f, 1f);
            // smoothstep
            ht = ht * ht * (3f - 2f * ht);

            // Slope promotes rocks
            float upDot = MathF.Max(0f, Vector3.Dot(Vector3.UnitY, Vector3.Normalize(normal)));
            float slope = 1f - upDot; // 0 flat, 1 vertical
            float rockBoost = Math.Clamp((slope - 0.35f) / 0.35f, 0f, 1f); // cliffs -> more rocks
            ht = Math.Clamp(ht + rockBoost * 0.5f, 0f, 1f);

            // Sample both textures (average to reduce noise)
            Color lowCol = _lowAvailable ? AverageTextureSample(_lowImage, _lowW, _lowH, _lowTiling, cx, cz, 0.35f) : biome;
            Color highCol = _highAvailable ? AverageTextureSample(_highImage, _highW, _highH, _highTiling, cx, cz, 0.35f) : biome;

            // Vibrance: mild saturation boost
            lowCol = Vibrance(lowCol, 0.15f);
            highCol = Vibrance(highCol, 0.18f);

            Color texBlend = Lerp(lowCol, highCol, ht);

            // Combine with biome base color
            float blendStrength = _textureBlend; // can reduce a bit on extreme slopes to keep lighting readable
            float slopeReduce = 1f - Math.Clamp((slope - 0.8f) / 0.2f, 0f, 1f) * 0.25f;
            blendStrength = Math.Clamp(blendStrength * slopeReduce, 0f, 1f);
            return Lerp(biome, texBlend, blendStrength);
        }

        private static Color SampleTexture(Image img, int w, int h, float tiling, float worldX, float worldZ)
        {
            if (w <= 0 || h <= 0)
                return new Color(160, 160, 160, 255);
            float u = worldX * tiling;
            float v = worldZ * tiling;
            u = u - MathF.Floor(u);
            v = v - MathF.Floor(v);
            int px = Math.Clamp((int)MathF.Floor(u * w), 0, w - 1);
            int py = Math.Clamp((int)MathF.Floor(v * h), 0, h - 1);
            unsafe { return Raylib.GetImageColor(img, px, py); }
        }

        private static Color AverageTextureSample(Image img, int w, int h, float tiling, float worldX, float worldZ, float radiusWorld)
        {
            Color c0 = SampleTexture(img, w, h, tiling, worldX, worldZ);
            Color c1 = SampleTexture(img, w, h, tiling, worldX + radiusWorld, worldZ);
            Color c2 = SampleTexture(img, w, h, tiling, worldX - radiusWorld, worldZ);
            Color c3 = SampleTexture(img, w, h, tiling, worldX, worldZ + radiusWorld);
            Color c4 = SampleTexture(img, w, h, tiling, worldX, worldZ - radiusWorld);
            int r = c0.r + c1.r + c2.r + c3.r + c4.r;
            int g = c0.g + c1.g + c2.g + c3.g + c4.g;
            int b = c0.b + c1.b + c2.b + c3.b + c4.b;
            int a = c0.a + c1.a + c2.a + c3.a + c4.a;
            return new Color((byte)(r / 5), (byte)(g / 5), (byte)(b / 5), (byte)(a / 5));
        }

        private static Color Desaturate(Color c, float amount)
        {
            amount = Math.Clamp(amount, 0f, 1f);
            float gray = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            byte r = (byte)MathF.Round(c.r + (gray - c.r) * amount);
            byte g = (byte)MathF.Round(c.g + (gray - c.g) * amount);
            byte b = (byte)MathF.Round(c.b + (gray - c.b) * amount);
            return new Color(r, g, b, c.a);
        }

        private static Color Vibrance(Color c, float amount)
        {
            // Increase saturation while preserving luminance approximately
            amount = Math.Clamp(amount, 0f, 1f);
            float gray = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
            float rf = c.r - gray;
            float gf = c.g - gray;
            float bf = c.b - gray;
            // Push away from gray
            float scale = 1f + amount * 0.9f;
            int r = (int)MathF.Round(gray + rf * scale);
            int g = (int)MathF.Round(gray + gf * scale);
            int b = (int)MathF.Round(gray + bf * scale);
            // Clamp
            r = Math.Clamp(r, 0, 255);
            g = Math.Clamp(g, 0, 255);
            b = Math.Clamp(b, 0, 255);
            return new Color((byte)r, (byte)g, (byte)b, c.a);
        }

        private static Color SampleLayerColor(float height, Vector3 normal)
        {
            // Basic biome layering: lower = dirt/sand, mid = grass, high/steep = rock/snow
            float slope = 1.0f - MathF.Max(0f, Vector3.Dot(Vector3.UnitY, Vector3.Normalize(normal))); // 0 flat, 1 vertical

            // Define palette
            Color sand = new Color(194, 178, 128, 255);
            Color dirt = new Color(120, 100, 80, 255);
            Color grass = new Color(60, 120, 60, 255);
            Color rock = new Color(120, 120, 120, 255);
            Color snow = new Color(235, 235, 235, 255);

            // Height thresholds (world units)
            float h0 = 0.5f;   // sand beach
            float h1 = 1.5f;   // dirt
            float h2 = 3.0f;   // grass
            float h3 = 5.5f;   // rock

            Color low = height < h0 ? sand : (height < h1 ? dirt : (height < h2 ? grass : (height < h3 ? rock : snow)));
            Color high = height < h0 ? dirt : (height < h1 ? grass : (height < h2 ? rock : snow));

            // Blend by fractional part within tier for smoother transitions
            float t = 0.0f;
            if (height < h0) t = Math.Clamp((height - 0.0f) / MathF.Max(0.0001f, h0 - 0.0f), 0f, 1f);
            else if (height < h1) t = Math.Clamp((height - h0) / MathF.Max(0.0001f, h1 - h0), 0f, 1f);
            else if (height < h2) t = Math.Clamp((height - h1) / MathF.Max(0.0001f, h2 - h1), 0f, 1f);
            else if (height < h3) t = Math.Clamp((height - h2) / MathF.Max(0.0001f, h3 - h2), 0f, 1f);
            else t = Math.Clamp((height - h3) / 3.0f, 0f, 1f);

            Color baseCol = Lerp(low, high, t);

            // Increase rock/snow influence on steep slopes
            float steep = Math.Clamp((slope - 0.4f) / 0.6f, 0f, 1f);
            Color steepCol = Lerp(baseCol, height > h2 ? rock : dirt, steep * 0.6f);
            return steepCol;
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

        private static Color Tint(Color c, float factor)
        {
            byte r = (byte)MathF.Round(c.r * factor);
            byte g = (byte)MathF.Round(c.g * factor);
            byte b = (byte)MathF.Round(c.b * factor);
            return new Color(r, g, b, c.a);
        }
    }
}

