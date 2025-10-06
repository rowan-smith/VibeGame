using System.Numerics;
using Raylib_CsLo;
using VibeGame.Terrain;
using VibeGame.Core;

namespace VibeGame
{
    public class VibeGameEngine : IGameEngine
    {
        private readonly ICameraController _cameraController;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _infiniteTerrain;
        private readonly ITextureManager _textureManager;

        public VibeGameEngine(ICameraController cameraController, IPhysicsController physics, IInfiniteTerrain infiniteTerrain, ITextureManager textureManager)
        {
            _cameraController = cameraController;
            _physics = physics;
            _infiniteTerrain = infiniteTerrain;
            _textureManager = textureManager;
        }

        public async Task RunAsync()
        {
            Raylib.InitWindow(1280, 720, "VibeGame - Simple Engine");
            Raylib.SetTargetFPS(75);
            Raylib.DisableCursor(); // capture mouse for FPS look

            // Now that GPU context exists, preload textures
            await _textureManager.PreloadAsync();

            // Basic FPS-style camera
            Camera3D camera = new Camera3D
            {
                position = new Vector3(0.2f, 3.0f, -6f),
                target = new Vector3(0.2f, 1.2f, -2f),
                up = new Vector3(0, 1, 0),
                fovy = 75f,
                projection_ = CameraProjection.CAMERA_PERSPECTIVE,
            };

            Color baseColor = new Color(182, 140, 102, 255);

            // Place camera on ground at start
            float startGround = _infiniteTerrain.SampleHeight(camera.position.X, camera.position.Z) + 1.7f;
            if (camera.position.Y < startGround)
                camera.position = new Vector3(camera.position.X, startGround, camera.position.Z);

            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();

                // Update chunk cache around camera
                // Render fewer surrounding chunks for better performance
                _infiniteTerrain.UpdateAround(camera.position, 1);

                // Input-driven orientation + desired horizontal move
                Vector3 horizMove = _cameraController.UpdateAndGetHorizontalMove(ref camera, dt);

                // Physics integration with ground sampling from infinite terrain
                _physics.Integrate(ref camera, dt, horizMove, (x, z) => _infiniteTerrain.SampleHeight(x, z));

                Raylib.BeginDrawing();
                Raylib.ClearBackground(Raylib.SKYBLUE);
                Raylib.BeginMode3D(camera);

                _infiniteTerrain.Render(camera, baseColor);

                Raylib.EndMode3D();
                Raylib.EndDrawing();
            }

            Raylib.CloseWindow();
        }
    }
}
