using System.Numerics;
using Raylib_cs;

namespace VibeGame
{
    public class VibeGameEngine : IGameEngine
    {
        private readonly ITerrainGenerator _terrain;
        private readonly ITerrainRenderer _terrainRenderer;
        private readonly ITreeRenderer _treeRenderer;

        public VibeGameEngine(ITerrainGenerator terrain, ITerrainRenderer terrainRenderer, ITreeRenderer treeRenderer)
        {
            _terrain = terrain;
            _terrainRenderer = terrainRenderer;
            _treeRenderer = treeRenderer;
        }

        public Task RunAsync()
        {
            Raylib.InitWindow(1280, 720, "VibeGame - Simple Engine");
            Raylib.SetTargetFPS(75);

            // Basic FPS-style camera
            Camera3D camera = new Camera3D
            {
                Position = new Vector3(0.2f, 3.0f, -6f),
                Target = new Vector3(0.2f, 1.2f, -2f),
                Up = new Vector3(0, 1, 0),
                FovY = 75f,
                Projection = CameraProjection.Perspective
            };

            float[,] heights = _terrain.GenerateHeights();
            float tileSize = _terrain.TileSize;
            Color baseColor = new Color(182, 140, 102, 255);

            // Generate a deterministic set of trees once for this terrain
            List<(Vector3 pos, float trunkHeight, float trunkRadius, float canopyRadius)> trees =
                _treeRenderer.GenerateTrees(_terrain, heights, 300);

            while (!Raylib.WindowShouldClose())
            {
                // Very simple camera controls (WASD + mouse)
                UpdateCameraBasic(ref camera);

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.SkyBlue);
                Raylib.BeginMode3D(camera);

                _terrainRenderer.Render(heights, tileSize, camera, baseColor);

                // Draw trees after terrain
                foreach (var t in trees)
                {
                    _treeRenderer.DrawTree(t.pos, t.trunkHeight, t.trunkRadius, t.canopyRadius);
                }

                Raylib.EndMode3D();
                Raylib.EndDrawing();
            }

            Raylib.CloseWindow();
            return Task.CompletedTask;
        }

        private static void UpdateCameraBasic(ref Camera3D camera)
        {
            float move = 0.15f;
            float rot = 0.003f;

            // Mouse look
            Vector2 mouseDelta = Raylib.GetMouseDelta();
            Vector3 forward = Vector3.Normalize(camera.Target - camera.Position);
            Vector3 right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));

            // Yaw
            Matrix4x4 yaw = Matrix4x4.CreateFromAxisAngle(camera.Up, -mouseDelta.X * rot);
            forward = Vector3.TransformNormal(forward, yaw);
            right = Vector3.TransformNormal(right, yaw);
            // Pitch (limit)
            Vector3 pitchAxis = right;
            Matrix4x4 pitch = Matrix4x4.CreateFromAxisAngle(pitchAxis, -mouseDelta.Y * rot);
            Vector3 newForward = Vector3.TransformNormal(forward, pitch);
            float yDot = Vector3.Dot(newForward, camera.Up);
            if (yDot > -0.95f && yDot < 0.95f) forward = newForward;

            // Keyboard
            if (Raylib.IsKeyDown(KeyboardKey.W)) camera.Position += forward * move;
            if (Raylib.IsKeyDown(KeyboardKey.S)) camera.Position -= forward * move;
            if (Raylib.IsKeyDown(KeyboardKey.A)) camera.Position -= right * move;
            if (Raylib.IsKeyDown(KeyboardKey.D)) camera.Position += right * move;

            camera.Target = camera.Position + forward;
        }
    }
}
