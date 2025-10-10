using System.Numerics;
using Raylib_CsLo;
using Veilborne.Core.Interfaces;
using VibeGame.Core.Items;
using VibeGame.Interfaces;
using VibeGame.Terrain;

namespace VibeGame.Core
{
    public class VibeGameEngine : IGameEngine
    {
        private readonly ICameraController _cameraController;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _terrain;
        private readonly IItemRegistry _items;

        private bool _paused = false;
        private bool _showDebugOverlay = false;
        private bool _showDebugChunkBounds = false;
        private int _selectedHotbarSlot = 0;
        private Veilborne.Core.GameWorlds.Terrain.Camera _camera;

        public VibeGameEngine(ICameraController cameraController, IPhysicsController physics, IInfiniteTerrain terrain, IItemRegistry items)
        {
            _cameraController = cameraController;
            _physics = physics;
            _terrain = terrain;
            _items = items;

            _camera = new Veilborne.Core.GameWorlds.Terrain.Camera(new Vector3(0, 5, -10), Vector3.Zero, Vector3.UnitY);
        }

        public async Task RunAsync()
        {
            Raylib.InitWindow(1280, 720, "VibeGame");
            Raylib.SetTargetFPS(60);

            // Hide and lock cursor for gameplay
            Raylib.DisableCursor();

            // Pre-warm terrain around initial camera position
            _terrain.UpdateCenter(_camera.Position);

            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();

                HandleInput();

                if (!_paused)
                {
                    // Camera and physics update
                    Vector3 horizMove = _cameraController.UpdateAndGetHorizontalMove(ref _camera.RaylibCamera, dt);
                    _physics.Integrate(ref _camera.RaylibCamera, dt, horizMove, (x, z) => _terrain.SampleHeight(new Vector3(x, 0, z)));

                    // Update terrain around camera
                    _terrain.UpdateCenter(_camera.Position);
                }

                // Rendering
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Raylib.SKYBLUE);

                // 3D world rendering
                Raylib.BeginMode3D(_camera.RaylibCamera);
                _terrain.Render(_camera, Raylib.GREEN);

                if (_showDebugChunkBounds)
                {
                    _terrain.RenderDebugChunkBounds(_camera);
                }

                Raylib.EndMode3D();

                // 2D UI rendering
                if (_showDebugOverlay)
                {
                    DrawDebugOverlay();
                }

                DrawHotbar();

                // Pause overlay
                if (_paused)
                {
                    DrawPauseMenu();
                }

                Raylib.EndDrawing();
            }

            Raylib.CloseWindow();
        }

        private void HandleInput()
        {
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
            {
                _paused = !_paused;
                // Toggle cursor visibility/lock based on pause state
                if (_paused)
                    Raylib.EnableCursor();
                else
                    Raylib.DisableCursor();
            }

            // Toggle debug overlay on F1 key press
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_F1))
            {
                _showDebugOverlay = !_showDebugOverlay;
            }
            
            // Toggle debug overlay on F2 key press
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_F2))
            {
                _showDebugChunkBounds = !_showDebugChunkBounds;
            }

            // Mouse wheel hotbar scroll (only when not paused)
            if (!_paused)
            {
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel != 0)
                {
                    int delta = wheel > 0 ? -1 : 1; // typical games: wheel up moves left
                    _selectedHotbarSlot = ((_selectedHotbarSlot + delta) % 9 + 9) % 9; // wrap 0..8
                }
            }

            // Hotbar selection 1-9
            for (int i = 0; i < 9; i++)
            {
                if (Raylib.IsKeyPressed((KeyboardKey)((int)KeyboardKey.KEY_ONE + i)))
                    _selectedHotbarSlot = i;
            }

            // Example dig action
            if (Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT) && !_paused)
            {
                if (_terrain is IEditableTerrain editable)
                {
                    Vector3 dir = Vector3.Normalize(_camera.Target - _camera.Position);
                    Vector3 digPos = _camera.Position + dir * 5f;
                    editable.DigSphereAsync(digPos, 1f, 1f, VoxelFalloff.Linear).Wait();
                }
            }
        }

        private void DrawDebugOverlay()
        {
            int fps = Raylib.GetFPS();
            var pos = _camera.Position;
            int x = 10;
            int y = 10;
            Raylib.DrawText($"FPS: {fps}", x, y, 20, Raylib.GREEN);
            Raylib.DrawText($"Pos: {pos.X:0.0}, {pos.Y:0.0}, {pos.Z:0.0}", x, y + 22, 20, Raylib.WHITE);

            if (_terrain is IDebugTerrain dbg)
            {
                var info = dbg.GetDebugInfo(pos);
                int line = y + 44;
                Raylib.DrawText($"Chunk: ({info.ChunkX}, {info.ChunkZ})", x, line, 20, Raylib.WHITE);
                line += 22;
                Raylib.DrawText($"Local: ({info.LocalX}, {info.LocalZ}) of {info.ChunkSize} (tile {info.TileSize:0.##}m)", x, line, 20, Raylib.WHITE);
                line += 22;
                Raylib.DrawText($"Biome: {info.BiomeId}", x, line, 20, Raylib.WHITE);
            }
        }

        private void DrawHotbar()
        {
            int slotSize = 60;
            int spacing = 5;
    
            // Calculate total width of hotbar
            int totalWidth = (slotSize * 9) + (spacing * 8);  // 9 slots + 8 spaces
    
            // Center the hotbar horizontally
            int startX = Raylib.GetScreenWidth() / 2 - totalWidth / 2;
            int startY = Raylib.GetScreenHeight() - slotSize - 10;

            for (int i = 0; i < 9; i++)
            {
                Rectangle slot = new Rectangle(startX + i * (slotSize + spacing), startY, slotSize, slotSize);
                Raylib.DrawRectangleRec(slot, Raylib.DARKGRAY);

                if (i == _selectedHotbarSlot)
                {
                    Raylib.DrawRectangleLines((int)slot.x, (int)slot.y, (int)slot.width, (int)slot.height, Raylib.YELLOW);
                }

                var item = _items.GetItemInSlot(i);
                if (item != null)
                {
                    // Draw item texture in slot
                }
            }
        }

        private void DrawPauseMenu()
        {
            int width = 300;
            int height = 200;
            int x = Raylib.GetScreenWidth() / 2 - width / 2;
            int y = Raylib.GetScreenHeight() / 2 - height / 2;

            Raylib.DrawRectangle(x, y, width, height, Raylib.BLACK);
            Raylib.DrawText("PAUSED", x + 100, y + 20, 40, Raylib.WHITE);
            Raylib.DrawText("Press ESC to resume", x + 30, y + 100, 20, Raylib.GRAY);
        }
    }
}
