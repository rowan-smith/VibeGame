using System.Numerics;
using Raylib_cs;

namespace VibeGame.Terrain
{
    // Renderer that draws a simple heightmap mesh and applies basic lighting and biome-like color layers.
    public class TerrainRenderer : ITerrainRenderer
    {
        public void Render(float[,] heights, float tileSize, Camera3D camera, Color baseColor)
        {
            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);
            int half = sizeX / 2;

            Vector3 camPos = camera.Position;
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

                    // Triangle 1 (p00, p01, p10)
                    Vector3 n1 = Vector3.Normalize(Vector3.Cross(p01 - p00, p10 - p00));
                    float diff1 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n1, lightDir)));
                    float hAvg1 = (h00 + h01 + h10) / 3f;
                    Color baseLayer1 = SampleLayerColor(hAvg1, n1);
                    Color c1 = Tint(baseLayer1, diff1);
                    Raylib.DrawTriangle3D(p00, p01, p10, c1);

                    // Triangle 2 (p01, p11, p10)
                    Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                    float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                    float hAvg2 = (h01 + h11 + h10) / 3f;
                    Color baseLayer2 = SampleLayerColor(hAvg2, n2);
                    Color c2 = Tint(baseLayer2, diff2);
                    Raylib.DrawTriangle3D(p01, p11, p10, c2);
                }
            }
        }

        public void RenderAt(float[,] heights, float tileSize, Vector2 originWorld, Camera3D camera, Color baseColor)
        {
            int sizeX = heights.GetLength(0);
            int sizeZ = heights.GetLength(1);

            Vector3 camPos = camera.Position;
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
                    float hAvg1 = (h00 + h01 + h10) / 3f;
                    Color baseLayer1 = SampleLayerColor(hAvg1, n1);
                    Color c1 = Tint(baseLayer1, diff1);
                    Raylib.DrawTriangle3D(p00, p01, p10, c1);

                    Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                    float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                    float hAvg2 = (h01 + h11 + h10) / 3f;
                    Color baseLayer2 = SampleLayerColor(hAvg2, n2);
                    Color c2 = Tint(baseLayer2, diff2);
                    Raylib.DrawTriangle3D(p01, p11, p10, c2);
                }
            }
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
            byte r = (byte)MathF.Round(a.R + (b.R - a.R) * t);
            byte g = (byte)MathF.Round(a.G + (b.G - a.G) * t);
            byte bch = (byte)MathF.Round(a.B + (b.B - a.B) * t);
            byte aCh = (byte)MathF.Round(a.A + (b.A - a.A) * t);
            return new Color(r, g, bch, aCh);
        }

        private static Color Tint(Color c, float factor)
        {
            byte r = (byte)MathF.Round(c.R * factor);
            byte g = (byte)MathF.Round(c.G * factor);
            byte b = (byte)MathF.Round(c.B * factor);
            return new Color(r, g, b, c.A);
        }
    }
}
