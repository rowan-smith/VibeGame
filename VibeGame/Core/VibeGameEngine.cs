using System.Numerics;
using ZeroElectric.Vinculum;
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
        private readonly ITextureManager _textureManager;

        private bool _showDebugOverlay = false;
        private bool _showDebugChunkBounds = false;
        private int _selectedHotbarSlot = 0;
        private Veilborne.Core.GameWorlds.Terrain.Camera _camera;

        // Window state for borderless toggle
        private bool _isBorderless = false;
        private int _windowedWidth = 1280;
        private int _windowedHeight = 720;

        // Simple UI state machine
        private enum GameState { Initialization, MainMenu, Loading, Playing, Paused }
        private GameState _state = GameState.MainMenu;

        // UI assets
        private Texture _logoTexture;
        private Texture _splashTexture;

        // Loading state
        private float _loadingProgress;
        private double _loadingTime;
        private const double LoadingDurationSeconds = 1.5; // heuristic warmup time
        private bool _requestedExit;

        // Initialization splash timing
        private double _initTime;
        private const double InitDurationSeconds = 0.75;

        public VibeGameEngine(ICameraController cameraController, IPhysicsController physics, IInfiniteTerrain terrain, IItemRegistry items, ITextureManager textureManager)
        {
            _cameraController = cameraController;
            _physics = physics;
            _terrain = terrain;
            _items = items;
            _textureManager = textureManager;

            _camera = new Veilborne.Core.GameWorlds.Terrain.Camera(new Vector3(0, 5, -10), Vector3.Zero, Vector3.UnitY);
        }

        public async Task RunAsync()
        {
            Raylib.InitWindow(1280, 720, "VibeGame");
            Raylib.SetTargetFPS(60);
            Raylib.SetExitKey(KeyboardKey.KEY_NULL);

            // Load UI assets (logo/splash + set window icon)
            LoadUiAssets();

            // Start staged preload and show initialization screen while it runs
            _state = GameState.Initialization;
            _initTime = 0;
            Raylib.EnableCursor();
            _textureManager.BeginPreload();

            while (!Raylib.WindowShouldClose())
            {
                float dt = Raylib.GetFrameTime();

                HandleInput(dt);

                // Update based on state
                if (_state == GameState.Playing)
                {
                    // Camera and physics update when playing
                    Vector3 horizMove = _cameraController.UpdateAndGetHorizontalMove(ref _camera.RaylibCamera, dt);
                    _physics.Integrate(ref _camera.RaylibCamera, dt, horizMove, (x, z) => _terrain.SampleHeight(new Vector3(x, 0, z)));

                    // Update terrain around camera
                    _terrain.UpdateCenter(_camera.Position);
                }
                else if (_state == GameState.Initialization)
                {
                    // Pump staged texture preload while showing initialization screen
                    _textureManager.PumpPreload(32);
                    if (!_textureManager.IsPreloading)
                    {
                        _state = GameState.MainMenu;
                    }
                }
                else if (_state == GameState.Loading)
                {
                    // Warm up terrain around initial camera position for a short time
                    _terrain.UpdateCenter(_camera.Position);
                    _loadingTime += dt;
                    _loadingProgress = (float)Math.Clamp(_loadingTime / LoadingDurationSeconds, 0.0, 1.0);
                    if (_loadingProgress >= 1.0f)
                    {
                        _state = GameState.Playing;
                        _loadingTime = 0;
                        _loadingProgress = 0;
                        Raylib.DisableCursor();
                    }
                }

                // Rendering
                Raylib.BeginDrawing();
                Raylib.ClearBackground(Raylib.SKYBLUE);

                // 3D world rendering only during Playing or Paused (show world behind pause menu)
                if (_state == GameState.Playing || _state == GameState.Paused)
                {
                    Raylib.BeginMode3D(_camera.RaylibCamera);
                    _terrain.Render(_camera);

                    if (_showDebugChunkBounds)
                    {
                        _terrain.RenderDebugChunkBounds(_camera);
                    }

                    Raylib.EndMode3D();
                }

                // 2D UI overlays
                switch (_state)
                {
                    case GameState.Initialization:
                        DrawInitializationScreen();
                        break;
                    case GameState.MainMenu:
                        DrawMainMenu();
                        break;
                    case GameState.Loading:
                        DrawLoadingScreen();
                        break;
                    case GameState.Playing:
                        if (_showDebugOverlay) DrawDebugOverlay();
                        DrawCrosshair();
                        DrawHotbar();
                        break;
                    case GameState.Paused:
                        // Dim the scene then draw pause menu
                        DrawPauseOverlay();
                        DrawPauseMenu();
                        break;
                }

                Raylib.EndDrawing();

                if (_requestedExit)
                {
                    break;
                }
            }

            Raylib.CloseWindow();
        }

        private void HandleInput(float dt)
        {
            // Global toggles (available in all states)
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_F1)) _showDebugOverlay = !_showDebugOverlay;
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_F2)) _showDebugChunkBounds = !_showDebugChunkBounds;

            // Toggle borderless fullscreen on F12 key press
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_F12))
            {
                if (!_isBorderless)
                {
                    _windowedWidth = Raylib.GetScreenWidth();
                    _windowedHeight = Raylib.GetScreenHeight();
                    Raylib.ClearWindowState(ConfigFlags.FLAG_FULLSCREEN_MODE);
                    Raylib.SetWindowState(ConfigFlags.FLAG_WINDOW_UNDECORATED);
                    int monitor = Raylib.GetCurrentMonitor();
                    int monWidth = Raylib.GetMonitorWidth(monitor);
                    int monHeight = Raylib.GetMonitorHeight(monitor);
                    Vector2 monPos = Raylib.GetMonitorPosition(monitor);
                    Raylib.SetWindowSize(monWidth, monHeight);
                    Raylib.SetWindowPosition((int)monPos.X, (int)monPos.Y);
                    _isBorderless = true;
                }
                else
                {
                    Raylib.ClearWindowState(ConfigFlags.FLAG_WINDOW_UNDECORATED);
                    Raylib.SetWindowSize(_windowedWidth, _windowedHeight);
                    int monitor = Raylib.GetCurrentMonitor();
                    int monWidth = Raylib.GetMonitorWidth(monitor);
                    int monHeight = Raylib.GetMonitorHeight(monitor);
                    Vector2 monPos = Raylib.GetMonitorPosition(monitor);
                    int x = (int)monPos.X + (monWidth - _windowedWidth) / 2;
                    int y = (int)monPos.Y + (monHeight - _windowedHeight) / 2;
                    Raylib.SetWindowPosition(x, y);
                    _isBorderless = false;
                }
            }

            switch (_state)
            {
                case GameState.MainMenu:
                    // Keyboard shortcuts
                    break;

                case GameState.Loading:
                    // Allow cancel back to menu if needed
                    if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
                    {
                        _state = GameState.MainMenu;
                        _loadingTime = 0; _loadingProgress = 0;
                        Raylib.EnableCursor();
                    }
                    break;

                case GameState.Playing:
                    if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
                    {
                        _state = GameState.Paused;
                        Raylib.EnableCursor();
                        break;
                    }

                    // Mouse wheel hotbar scroll
                    float wheel = Raylib.GetMouseWheelMove();
                    if (wheel != 0)
                    {
                        int delta = wheel > 0 ? -1 : 1;
                        _selectedHotbarSlot = ((_selectedHotbarSlot + delta) % 9 + 9) % 9;
                    }

                    // Hotbar selection 1-9
                    for (int i = 0; i < 9; i++)
                    {
                        if (Raylib.IsKeyPressed((KeyboardKey)((int)KeyboardKey.KEY_ONE + i)))
                            _selectedHotbarSlot = i;
                    }

                    // Example dig action
                    if (Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
                    {
                        if (_terrain is IEditableTerrain editable)
                        {
                            // Only dig if we are looking at the ground in front of us
                            if (TryGetGroundHit(6f, 0.25f, 0.05f, out var hit))
                            {
                                editable.DigSphereAsync(hit, 1f, 1f, VoxelFalloff.Linear).Wait();
                            }
                        }
                    }
                    break;

                case GameState.Paused:
                    if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
                    {
                        _state = GameState.Playing;
                        Raylib.DisableCursor();
                    }
                    break;
            }
        }

        private void LoadUiAssets()
        {
            // Load both splash and logo at generous raster sizes for crisp scaling on high-res displays
            SvgTextureLoader.TryGetTexture("assets\\splash.svg", 2000, 1200, out _splashTexture);
            SvgTextureLoader.TryGetTexture("assets\\logo.svg", 1600, 800, out _logoTexture);

            // Set window icon from logo if available
            if (SvgTextureLoader.TryGetIconImage("assets\\logo.svg", 256, out var iconImg))
            {
                try
                {
                    Raylib.SetWindowIcon(iconImg);
                }
                finally
                {
                    Raylib.UnloadImage(iconImg);
                }
            }
        }

        private void StartGame()
        {
            // Reset camera position if needed
            _camera.RaylibCamera.position = new Vector3(0, 5, -10);
            _camera.RaylibCamera.target = Vector3.Zero;

            // Begin loading/warmup
            _state = GameState.Loading;
            _loadingTime = 0;
            _loadingProgress = 0;
        }

        private void DrawMainMenu()
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();

            // Background
            Raylib.ClearBackground(new Color(15, 18, 22, 255));

            // Choose logo if available, else fallback to splash
            Texture art = (_logoTexture.id != 0) ? _logoTexture : _splashTexture;

            // Draw art centered near top
            int centerY = (int)(h * 0.22f);
            int texW = art.width;
            int texH = art.height;
            int maxW = (int)(w * 0.6f);
            int maxH = (int)(h * 0.32f); // increase size for menu
            float scale = MathF.Min(maxW / MathF.Max(1, texW), maxH / MathF.Max(1, texH));
            int drawW = (int)(Math.Max(1, texW) * scale);
            int drawH = (int)(Math.Max(1, texH) * scale);
            int x = w / 2 - drawW / 2;
            int y = centerY - drawH / 2;
            var src = new Rectangle(0, 0, texW, texH);
            var dst = new Rectangle(x, y, drawW, drawH);
            if (texW > 0 && texH > 0)
                Raylib.DrawTexturePro(art, src, dst, new Vector2(0, 0), 0, Raylib.WHITE);

            // Buttons
            int btnW = Math.Min(360, (int)(w * 0.4f));
            int btnH = 60;
            int xCenter = w / 2 - btnW / 2;
            int firstY = (int)(h * 0.5f);
            Rectangle startRect = new Rectangle(xCenter, firstY, btnW, btnH);
            Rectangle exitRect = new Rectangle(xCenter, firstY + btnH + 16, btnW, btnH);

            if (Button("Start", startRect))
            {
                StartGame();
            }
            if (Button("Exit", exitRect))
            {
                _requestedExit = true;
            }
        }

        private void DrawLoadingScreen()
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            Raylib.ClearBackground(new Color(10, 12, 16, 255));

            string title = "Loading terrain...";
            int tw = Raylib.MeasureText(title, 30);
            Raylib.DrawText(title, w / 2 - tw / 2, h / 2 - 80, 30, Raylib.RAYWHITE);

            // Progress bar
            int barW = Math.Min(500, (int)(w * 0.6f));
            int barH = 24;
            int x = w / 2 - barW / 2;
            int y = h / 2 - barH / 2;
            Raylib.DrawRectangle(x, y, barW, barH, new Color(30, 35, 42, 255));
            int filled = (int)(barW * Math.Clamp(_loadingProgress, 0f, 1f));
            Raylib.DrawRectangle(x, y, filled, barH, new Color(100, 200, 255, 255));
            Raylib.DrawRectangleLines(x, y, barW, barH, Raylib.GRAY);
        }

        private void DrawInitializationScreen()
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            Raylib.ClearBackground(new Color(10, 12, 16, 255));

            // Draw splash centered and get its bottom Y to position UI below it
            int texW = _splashTexture.width;
            int texH = _splashTexture.height;
            int maxW = (int)(w * 0.85f);   // larger init splash
            int maxH = (int)(h * 0.65f);
            float scale = MathF.Min(maxW / (float)Math.Max(texW, 1), maxH / (float)Math.Max(texH, 1));
            int drawW = (int)(Math.Max(1, texW) * scale);
            int drawH = (int)(Math.Max(1, texH) * scale);
            int x = w / 2 - drawW / 2;
            int y = h / 2 - drawH / 2 - 10;
            var src = new Rectangle(0, 0, texW, texH);
            var dst = new Rectangle(x, y, drawW, drawH);
            if (texW > 0 && texH > 0)
            {
                Raylib.DrawTexturePro(_splashTexture, src, dst, new Vector2(0, 0), 0, Raylib.WHITE);
            }
            int contentBottom = y + drawH;

            // Progress UI
            float p = _textureManager.PreloadProgress;
            string stage = _textureManager.PreloadStage ?? "";

            string title = stage != "Complete" ? "Initializing Textures..." : "Initializing VibeGame...";
            int tw2 = Raylib.MeasureText(title, 24);

            // Place the title and bar just below the splash/logo with some margin and keep on screen
            int barW = Math.Min(520, (int)(w * 0.6f));
            int barH = 24;
            int marginAboveBar = 40;    // gap between title and bar
            int marginBelowArt = 28;    // gap between splash and title

            int titleY = contentBottom + marginBelowArt;
            int maxTitleY = Math.Max(0, h - (barH + marginAboveBar + 20 + 30 + 24)); // ensure stage text fits
            if (titleY > maxTitleY) titleY = maxTitleY;
            if (titleY < (int)(h * 0.6f)) titleY = (int)(h * 0.6f); // keep roughly lower third if art is small

            Raylib.DrawText(title, w / 2 - tw2 / 2, titleY, 24, Raylib.RAYWHITE);

            // Progress bar centered under the title
            int bx = w / 2 - barW / 2;
            int by = titleY + marginAboveBar;
            Raylib.DrawRectangle(bx, by, barW, barH, new Color(30, 35, 42, 255));
            int filled = (int)(barW * Math.Clamp(p, 0f, 1f));
            Raylib.DrawRectangle(bx, by, filled, barH, new Color(100, 200, 255, 255));
            Raylib.DrawRectangleLines(bx, by, barW, barH, Raylib.GRAY);

            // Stage text and percent
            string pct = $"{Math.Clamp((int)(p * 100), 0, 100)}%";
            string stageText = string.IsNullOrWhiteSpace(stage) ? pct : $"{stage}  {pct}";
            int stw = Raylib.MeasureText(stageText, 20);
            Raylib.DrawText(stageText, w / 2 - stw / 2, by + barH + 10, 20, Raylib.RAYWHITE);
        }

        private void DrawPauseOverlay()
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            Raylib.DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 160));
        }

        private void DrawPauseMenu()
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();

            int btnW = Math.Min(380, (int)(w * 0.4f));
            int btnH = 56;
            int xCenter = w / 2 - btnW / 2;
            int startY = (int)(h * 0.4f);

            if (Button("Resume", new Rectangle(xCenter, startY, btnW, btnH)))
            {
                _state = GameState.Playing;
                Raylib.DisableCursor();
                return;
            }
            if (Button("Exit to Menu", new Rectangle(xCenter, startY + btnH + 14, btnW, btnH)))
            {
                _state = GameState.MainMenu;
                Raylib.EnableCursor();
                return;
            }
            if (Button("Exit to Desktop", new Rectangle(xCenter, startY + (btnH + 14) * 2, btnW, btnH)))
            {
                _requestedExit = true;
                return;
            }
        }

        private bool Button(string text, Rectangle rect, int fontSize = 28)
        {
            Vector2 mouse = Raylib.GetMousePosition();
            bool hover = Raylib.CheckCollisionPointRec(mouse, rect);
            bool click = hover && Raylib.IsMouseButtonReleased(MouseButton.MOUSE_BUTTON_LEFT);

            Color bg = hover ? new Color(60, 70, 85, 255) : new Color(40, 46, 56, 255);
            Color fg = Raylib.RAYWHITE;

            Raylib.DrawRectangleRec(rect, bg);
            Raylib.DrawRectangleLines((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height, new Color(90, 100, 115, 255));

            int tw = Raylib.MeasureText(text, fontSize);
            int tx = (int)rect.x + (int)rect.width / 2 - tw / 2;
            int ty = (int)rect.y + (int)rect.height / 2 - fontSize / 2;
            Raylib.DrawText(text, tx, ty, fontSize, fg);
            return click;
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
            int totalWidth = (slotSize * 9) + (spacing * 8);
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
                    // Draw item texture in slot (placeholder)
                }
            }
        }

        private void DrawCrosshair()
        {
            int cx = Raylib.GetScreenWidth() / 2;
            int cy = Raylib.GetScreenHeight() / 2;
            int size = 6;

            // Determine color based on whether we are aiming at diggable ground
            Color color = new Color(230, 230, 230, 255);
            if (_state == GameState.Playing && TryGetGroundHit(6f, 0.25f, 0.05f, out _))
            {
                color = Raylib.GREEN;
            }

            Raylib.DrawLine(cx - size, cy, cx + size, cy, color);
            Raylib.DrawLine(cx, cy - size, cx, cy + size, color);
        }

        private bool TryGetGroundHit(float maxDistance, float step, float epsilon, out Vector3 hit)
        {
            hit = default;

            // Forward direction of camera
            Vector3 dir = Vector3.Normalize(_camera.Target - _camera.Position);

            // Require some downward component to be considered "looking at the ground"
            float downDot = Vector3.Dot(dir, Vector3.UnitY);
            if (downDot > -0.15f) // not looking down enough
            {
                return false;
            }

            float traveled = 0f;
            Vector3 p = _camera.Position;
            while (traveled <= maxDistance)
            {
                p += dir * step;
                traveled += step;

                float groundY = _terrain.SampleHeight(new Vector3(p.X, 0, p.Z));
                if (p.Y <= groundY + epsilon)
                {
                    hit = new Vector3(p.X, groundY, p.Z);
                    return true;
                }
            }
            return false;
        }
    }
}
