using System.Numerics;
using Raylib_cs;

namespace VibeGame
{
    public class VibeGameEngine : IGameEngine
    {
        private readonly ICameraController _cameraController;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _infiniteTerrain;

        public VibeGameEngine(ICameraController cameraController, IPhysicsController physics, IInfiniteTerrain infiniteTerrain)
        {
            _cameraController = cameraController;
            _physics = physics;
            _infiniteTerrain = infiniteTerrain;
        }

        public Task RunAsync()
        {
            Raylib.InitWindow(1280, 720, "VibeGame - Simple Engine");
            Raylib.SetTargetFPS(75);
            Raylib.DisableCursor(); // capture mouse for FPS look

            // Basic FPS-style camera
            Camera3D camera = new Camera3D
            {
                Position = new Vector3(0.2f, 3.0f, -6f),
                Target = new Vector3(0.2f, 1.2f, -2f),
                Up = new Vector3(0, 1, 0),
                FovY = 75f,
                Projection = CameraProjection.Perspective
            };

            Color baseColor = new Color(182, 140, 102, 255);

            // Place camera on ground at start
            float startGround = _infiniteTerrain.SampleHeight(camera.Position.X, camera.Position.Z) + 1.7f;
            if (camera.Position.Y < startGround)
                camera.Position = new Vector3(camera.Position.X, startGround, camera.Position.Z);

            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();

                // Update chunk cache around camera
                _infiniteTerrain.UpdateAround(camera.Position, 2);

                // Input-driven orientation + desired horizontal move
                Vector3 horizMove = _cameraController.UpdateAndGetHorizontalMove(ref camera, dt);

                // Physics integration with ground sampling from infinite terrain
                _physics.Integrate(ref camera, dt, horizMove, (x, z) => _infiniteTerrain.SampleHeight(x, z));

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Color.SkyBlue);
                Raylib.BeginMode3D(camera);

                _infiniteTerrain.Render(camera, baseColor);

                Raylib.EndMode3D();
                Raylib.EndDrawing();
            }

            Raylib.CloseWindow();
            return Task.CompletedTask;
        }
    }
}
