using System.Numerics;
using Raylib_cs;

namespace VibeGame.Terrain
{
    // Minimal renderer that draws triangles without rlgl immediate texture calls.
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
                    Color c1 = Tint(baseColor, diff1);
                    Raylib.DrawTriangle3D(p00, p01, p10, c1);

                    // Triangle 2 (p01, p11, p10)
                    Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                    float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                    Color c2 = Tint(baseColor, diff2);
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
                    Color c1 = Tint(baseColor, diff1);
                    Raylib.DrawTriangle3D(p00, p01, p10, c1);

                    Vector3 n2 = Vector3.Normalize(Vector3.Cross(p11 - p01, p10 - p01));
                    float diff2 = MathF.Max(0.35f, MathF.Min(1.0f, Vector3.Dot(n2, lightDir)));
                    Color c2 = Tint(baseColor, diff2);
                    Raylib.DrawTriangle3D(p01, p11, p10, c2);
                }
            }
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
