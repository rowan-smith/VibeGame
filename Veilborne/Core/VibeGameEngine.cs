using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.Terrain;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Items;
using Veilborne.Objects;
using Veilborne.Core.RaylibImpl;

namespace Veilborne.Core
{
    public class VibeGameEngine : IGameEngine
    {
        private readonly ICameraController _cameraController;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _terrain;
        private readonly IItemRegistry _items;
        private readonly ITextureManager _textureManager;
        private readonly ITimeService _time;
        private readonly IWorldObjectRenderer _worldObjectRenderer;
        private readonly EntityRegistry _entities;
        private readonly IGraphicsProvider _graphics;
        private readonly IInputProvider _input;
        private readonly IUiProvider _ui;
        private readonly List<ISystem> _updateSystems = new();
        private readonly List<IRenderSystem> _renderSystems = new();

        private bool _showDebugOverlay = false;
        private bool _showDebugChunkBounds = false;
        private int _selectedHotbarSlot = 0;
        private Entity _playerEntity = default!;

        // Simple UI state machine
        private enum GameState { Initialization, MainMenu, Loading, Playing, Paused }
        private GameState _state = GameState.MainMenu;

        // UI asset keys
        private const string LogoTextureKey = "ui/logo";
        private const string SplashTextureKey = "ui/splash";

        // Loading state
        private float _loadingProgress;
        private double _loadingTime;
        private const double LoadingDurationSeconds = 1.5; // heuristic warmup time
        private bool _requestedExit;

        // Initialization splash timing
        private const double InitDurationSeconds = 0.75;

        public VibeGameEngine(
            ICameraController cameraController,
            IPhysicsController physics,
            IInfiniteTerrain terrain,
            IItemRegistry items,
            ITextureManager textureManager,
            ITimeService time,
            IWorldObjectRenderer worldObjectRenderer,
            EntityRegistry entities,
            IGraphicsProvider graphics,
            IInputProvider input,
            IUiProvider ui,
            IEnumerable<ISystem> updateSystems,
            IEnumerable<IRenderSystem> renderSystems)
        {
            _cameraController = cameraController;
            _physics = physics;
            _terrain = terrain;
            _items = items;
            _textureManager = textureManager;
            _time = time;
            _worldObjectRenderer = worldObjectRenderer;
            _entities = entities;
            _graphics = graphics;
            _input = input;
            _ui = ui;
            _updateSystems.AddRange(updateSystems);
            _renderSystems.AddRange(renderSystems);
        }

        public async Task RunAsync()
        {
            _graphics.InitializeWindow(1280, 720, "Veilborne");
            _graphics.SetTargetFps(60);

            // Load UI assets (logo/splash + set window icon)
            LoadUiAssets();

            // Create player entity
            _playerEntity = _entities.CreateEntity();
            _playerEntity.AddComponent(new PlayerComponent());
            var transform = new TransformComponent { Position = new Vector3(0, 5, -10) };
            _playerEntity.AddComponent(transform);
            _playerEntity.AddComponent(new PhysicsComponent { CollisionRadius = 0.5f, IsStatic = false });
            var cameraComp = new CameraComponent
            {
                Position = transform.Position,
                Target = Vector3.Zero,
                Up = Vector3.UnitY,
                FovY = 45.0f
            };
            _playerEntity.AddComponent(cameraComp);

            // Start staged preload and show initialization screen while it runs
            _state = GameState.Initialization;
            _input.ShowCursor();
            _textureManager.BeginPreload();

            while (!_graphics.ShouldClose() && !_requestedExit)
            {
                float dt = _graphics.GetFrameTime();
                _time.Update(dt);
                float gameDt = _time.DeltaTime;

                HandleInput(gameDt);

                // Update based on state
                if (_state == GameState.Playing)
                {
                    // Execute all update systems (includes PlayerSystem, TerrainUpdateSystem etc)
                    foreach (var system in _updateSystems)
                        system.Update(gameDt);
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
                    var cam = _playerEntity.GetComponent<CameraComponent>();
                    // Warm up terrain around initial camera position for a short time
                    _terrain.UpdateCenter(cam.Position);
                    _loadingTime += dt;
                    _loadingProgress = (float)Math.Clamp(_loadingTime / LoadingDurationSeconds, 0.0, 1.0);
                    if (_loadingProgress >= 1.0f)
                    {
                        _state = GameState.Playing;
                        _loadingTime = 0;
                        _loadingProgress = 0;
                        _input.HideCursor();
                    }
                }

                // Rendering
                _graphics.BeginDrawing();
                _graphics.Clear(new Vector3(0.53f, 0.81f, 0.92f)); // Skyblue

                // 3D world rendering only during Playing or Paused (show world behind pause menu)
                if (_state == GameState.Playing || _state == GameState.Paused)
                {
                    var cam = _playerEntity.GetComponent<CameraComponent>();
                    _graphics.Begin3D(cam);
                    
                    // Execute all render systems (includes TerrainRenderSystem, WorldObjectRenderer etc)
                    foreach (var renderSystem in _renderSystems)
                        renderSystem.Draw();

                    if (_showDebugChunkBounds)
                    {
                        _terrain.RenderDebugChunkBounds(cam);
                    }

                    _graphics.End3D();
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

                _graphics.EndDrawing();
            }

            _graphics.CloseWindow();
        }

        private void HandleInput(float dt)
        {
            // Global toggles (available in all states)
            if (_input.IsKeyPressed(InputKeys.KEY_F1)) _showDebugOverlay = !_showDebugOverlay;
            if (_input.IsKeyPressed(InputKeys.KEY_F2)) _showDebugChunkBounds = !_showDebugChunkBounds;

            // Toggle borderless fullscreen on F12 key press
            if (_input.IsKeyPressed(InputKeys.KEY_F12))
            {
                _graphics.ToggleBorderless();
            }

            switch (_state)
            {
                case GameState.MainMenu:
                    // Keyboard shortcuts
                    break;

                case GameState.Loading:
                    // Allow cancel back to menu if needed
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        _state = GameState.MainMenu;
                        _loadingTime = 0; _loadingProgress = 0;
                        _input.ShowCursor();
                    }
                    break;

                case GameState.Playing:
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        _state = GameState.Paused;
                        _input.ShowCursor();
                        break;
                    }

                    // Mouse wheel hotbar scroll
                    float wheel = _input.GetMouseWheelMove();
                    if (wheel != 0)
                    {
                        int delta = wheel > 0 ? -1 : 1;
                        _selectedHotbarSlot = ((_selectedHotbarSlot + delta) % 9 + 9) % 9;
                    }

                    // Hotbar selection 1-9
                    for (int i = 0; i < 9; i++)
                    {
                        if (_input.IsKeyPressed(InputKeys.KEY_ONE + i))
                            _selectedHotbarSlot = i;
                    }

                    // Example dig action
                    if (_input.IsMouseButtonDown(InputKeys.MOUSE_BUTTON_LEFT))
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
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        _state = GameState.Playing;
                        _input.HideCursor();
                    }
                    break;
            }
        }

        private void LoadUiAssets()
        {
            if (SvgTextureLoader.TryGetTexture("assets\\splash.svg", 2000, 1200, out var splash))
            {
                _textureManager.Register(SplashTextureKey, splash);
            }
            if (SvgTextureLoader.TryGetTexture("assets\\logo.svg", 1600, 800, out var logo))
            {
                _textureManager.Register(LogoTextureKey, logo);
            }

            _graphics.SetWindowIcon("assets\\logo.svg");
        }

        private void StartGame()
        {
            var cam = _playerEntity.GetComponent<CameraComponent>();
            cam.Position = new Vector3(0, 5, -10);
            cam.Target = Vector3.Zero;

            // Begin loading/warmup
            _state = GameState.Loading;
            _loadingTime = 0;
            _loadingProgress = 0;
        }

        private void DrawMainMenu()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;

            // Background
            _graphics.Clear(new Vector3(15 / 255f, 18 / 255f, 22 / 255f));

            // Choose logo if available, else fallback to splash
            string artKey = _textureManager.TryGet(LogoTextureKey, out _) ? LogoTextureKey : SplashTextureKey;

            // Draw art centered near top
            int centerY = (int)(h * 0.22f);
            int texW = 1600; // placeholder for scale calculation
            int texH = 800;
            int maxW = (int)(w * 0.6f);
            int maxH = (int)(h * 0.32f); 
            float scale = MathF.Min(maxW / (float)Math.Max(1, texW), maxH / (float)Math.Max(1, texH));
            int drawW = (int)(Math.Max(1, texW) * scale);
            int drawH = (int)(Math.Max(1, texH) * scale);
            int x = w / 2 - drawW / 2;
            int y = centerY - drawH / 2;
            
            _ui.DrawTexture(artKey, x, y, scale, Vector4.One);

            // Buttons
            int btnW = Math.Min(360, (int)(w * 0.4f));
            int btnH = 60;
            int xCenter = w / 2 - btnW / 2;
            int firstY = (int)(h * 0.5f);
            Rect startRect = new Rect(xCenter, firstY, btnW, btnH);
            Rect exitRect = new Rect(xCenter, firstY + btnH + 16, btnW, btnH);

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
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _graphics.Clear(new Vector3(10 / 255f, 12 / 255f, 16 / 255f));

            string title = "Loading terrain...";
            int tw = _ui.MeasureText(title, 30);
            _ui.DrawText(title, w / 2 - tw / 2, h / 2 - 80, 30, Vector4.One);

            // Progress bar
            int barW = Math.Min(500, (int)(w * 0.6f));
            int barH = 24;
            int x = w / 2 - barW / 2;
            int y = h / 2 - barH / 2;
            _ui.DrawRectangle(x, y, barW, barH, new Vector4(30 / 255f, 35 / 255f, 42 / 255f, 1.0f));
            int filled = (int)(barW * Math.Clamp(_loadingProgress, 0f, 1f));
            _ui.DrawRectangle(x, y, filled, barH, new Vector4(100 / 255f, 200 / 255f, 255 / 255f, 1.0f));
            _ui.DrawRectangleLines(x, y, barW, barH, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));
        }

        private void DrawInitializationScreen()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _graphics.Clear(new Vector3(10 / 255f, 12 / 255f, 16 / 255f));

            // Draw splash centered and get its bottom Y to position UI below it
            int texW = 2000; 
            int texH = 1200;
            int maxW = (int)(w * 0.85f);   // larger init splash
            int maxH = (int)(h * 0.65f);
            float scale = MathF.Min(maxW / (float)Math.Max(texW, 1), maxH / (float)Math.Max(texH, 1));
            int drawW = (int)(Math.Max(1, texW) * scale);
            int drawH = (int)(Math.Max(1, texH) * scale);
            int x = w / 2 - drawW / 2;
            int y = h / 2 - drawH / 2 - 10;
            
            _ui.DrawTexture(SplashTextureKey, x, y, scale, Vector4.One);
            int contentBottom = y + drawH;

            // Progress UI
            float p = _textureManager.PreloadProgress;
            string stage = _textureManager.PreloadStage ?? "";

            string title = stage != "Complete" ? "Initializing Textures..." : "Initializing Veilborne...";
            int tw2 = _ui.MeasureText(title, 24);

            // Place the title and bar just below the splash/logo with some margin and keep on screen
            int barW = Math.Min(520, (int)(w * 0.6f));
            int barH = 24;
            int marginAboveBar = 40;    // gap between title and bar
            int marginBelowArt = 28;    // gap between splash and title

            int titleY = contentBottom + marginBelowArt;
            int maxTitleY = Math.Max(0, h - (barH + marginAboveBar + 20 + 30 + 24)); // ensure stage text fits
            if (titleY > maxTitleY) titleY = maxTitleY;
            if (titleY < (int)(h * 0.6f)) titleY = (int)(h * 0.6f); // keep roughly lower third if art is small

            _ui.DrawText(title, w / 2 - tw2 / 2, titleY, 24, Vector4.One);

            // Progress bar centered under the title
            int bx = w / 2 - barW / 2;
            int by = titleY + marginAboveBar;
            _ui.DrawRectangle(bx, by, barW, barH, new Vector4(30 / 255f, 35 / 255f, 42 / 255f, 1.0f));
            int filled = (int)(barW * Math.Clamp(p, 0f, 1f));
            _ui.DrawRectangle(bx, by, filled, barH, new Vector4(100 / 255f, 200 / 255f, 255 / 255f, 1.0f));
            _ui.DrawRectangleLines(bx, by, barW, barH, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

            // Stage text and percent
            string pct = $"{Math.Clamp((int)(p * 100), 0, 100)}%";
            string stageText = string.IsNullOrWhiteSpace(stage) ? pct : $"{stage}  {pct}";
            int stw = _ui.MeasureText(stageText, 20);
            _ui.DrawText(stageText, w / 2 - stw / 2, by + barH + 10, 20, Vector4.One);
        }

        private void DrawPauseOverlay()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _ui.DrawRectangle(0, 0, w, h, new Vector4(0, 0, 0, 160 / 255f));
        }

        private void DrawPauseMenu()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;

            int btnW = Math.Min(380, (int)(w * 0.4f));
            int btnH = 56;
            int xCenter = w / 2 - btnW / 2;
            int startY = (int)(h * 0.4f);

            if (Button("Resume", new Rect(xCenter, startY, btnW, btnH)))
            {
                _state = GameState.Playing;
                _input.HideCursor();
                return;
            }
            if (Button("Exit to Menu", new Rect(xCenter, startY + btnH + 14, btnW, btnH)))
            {
                _state = GameState.MainMenu;
                _input.ShowCursor();
                return;
            }
            if (Button("Exit to Desktop", new Rect(xCenter, startY + (btnH + 14) * 2, btnW, btnH)))
            {
                _requestedExit = true;
                return;
            }
        }

        private bool Button(string text, Rect rect, int fontSize = 28)
        {
            Vector2 mouse = _input.GetMousePosition();
            bool hover = mouse.X >= rect.X && mouse.X <= rect.X + rect.Width &&
                         mouse.Y >= rect.Y && mouse.Y <= rect.Y + rect.Height;
            bool click = hover && _input.IsMouseButtonReleased(InputKeys.MOUSE_BUTTON_LEFT);

            Vector4 bg = hover ? new Vector4(60 / 255f, 70 / 255f, 85 / 255f, 1.0f) : new Vector4(40 / 255f, 46 / 255f, 56 / 255f, 1.0f);
            Vector4 fg = Vector4.One; // white

            _ui.DrawRectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, bg);
            _ui.DrawRectangleLines((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, new Vector4(90 / 255f, 100 / 255f, 115 / 255f, 1.0f));

            int tw = _ui.MeasureText(text, fontSize);
            int tx = (int)rect.X + (int)rect.Width / 2 - tw / 2;
            int ty = (int)rect.Y + (int)rect.Height / 2 - fontSize / 2;
            _ui.DrawText(text, tx, ty, fontSize, fg);
            return click;
        }

        private void DrawDebugOverlay()
        {
            int fps = _time.Fps;
            var cam = _playerEntity.GetComponent<CameraComponent>();
            var pos = cam.Position;
            int x = 10;
            int y = 10;
            _ui.DrawText($"FPS: {fps}", x, y, 20, new Vector4(0, 1, 0, 1));
            _ui.DrawText($"Pos: {pos.X:0.0}, {pos.Y:0.0}, {pos.Z:0.0}", x, y + 22, 20, Vector4.One);

            if (_terrain is IDebugTerrain dbg)
            {
                var info = dbg.GetDebugInfo(pos);
                int line = y + 44;
                _ui.DrawText($"Chunk: ({info.ChunkX}, {info.ChunkZ})", x, line, 20, Vector4.One);
                line += 22;
                _ui.DrawText($"Local: ({info.LocalX}, {info.LocalZ}) of {info.ChunkSize} (tile {info.TileSize:0.##}m)", x, line, 20, Vector4.One);
                line += 22;
                _ui.DrawText($"Biome: {info.BiomeId}", x, line, 20, Vector4.One);
            }
        }

        private void DrawHotbar()
        {
            int slotSize = 60;
            int spacing = 5;
            int totalWidth = (slotSize * 9) + (spacing * 8);
            int startX = _graphics.ScreenWidth / 2 - totalWidth / 2;
            int startY = _graphics.ScreenHeight - slotSize - 10;

            for (int i = 0; i < 9; i++)
            {
                _ui.DrawRectangle(startX + i * (slotSize + spacing), startY, slotSize, slotSize, new Vector4(0.2f, 0.2f, 0.2f, 1.0f));
                if (i == _selectedHotbarSlot)
                {
                    _ui.DrawRectangleLines(startX + i * (slotSize + spacing), startY, slotSize, slotSize, new Vector4(1, 1, 0, 1));
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
            int cx = _graphics.ScreenWidth / 2;
            int cy = _graphics.ScreenHeight / 2;
            int size = 6;

            // Determine color based on whether we are aiming at diggable ground
            Vector4 color = new Vector4(0.9f, 0.9f, 0.9f, 1.0f);
            if (_state == GameState.Playing && TryGetGroundHit(6f, 0.25f, 0.05f, out _))
            {
                color = new Vector4(0, 1, 0, 1);
            }

            _ui.DrawLine(cx - size, cy, cx + size, cy, color);
            _ui.DrawLine(cx, cy - size, cx, cy + size, color);
        }

        private bool TryGetGroundHit(float maxDistance, float step, float epsilon, out Vector3 hit)
        {
            hit = default;
            var cam = _playerEntity.GetComponent<CameraComponent>();

            // Forward direction of camera
            Vector3 dir = Vector3.Normalize(cam.Target - cam.Position);

            // Require some downward component to be considered "looking at the ground"
            float downDot = Vector3.Dot(dir, Vector3.UnitY);
            if (downDot > -0.15f) // not looking down enough
            {
                return false;
            }

            float traveled = 0f;
            Vector3 p = cam.Position;
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
