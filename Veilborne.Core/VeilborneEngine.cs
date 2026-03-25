using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.Terrain;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Items;
using Veilborne.Core.Settings;
using Veilborne.Objects;

namespace Veilborne.Core
{
    public class VeilborneEngine : IGameEngine
    {
        private readonly ICameraController _cameraController;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _terrain;
        private readonly IItemRegistry _items;
        private readonly ITimeService _time;
        private readonly EntityRegistry _entities;
        private readonly IGraphicsProvider _graphics;
        private readonly IGameLoopHost _loopHost;
        private readonly IInputProvider _input;
        private readonly IEcsRuntime _ecsRuntime;
        private readonly IGameSettingsService _settings;
        private readonly bool _isDevelopmentEnvironment;

        // These will be initialized by ECS manager after MonoGame is ready
        private IUiProvider _ui;
        private IWorldObjectRenderer _worldObjectRenderer;

        private bool _showDebugOverlay;
        private bool _showDebugChunkBounds;
        private bool _isFullscreenApplied;
        private int _selectedHotbarSlot = 0;
        private Entity _playerEntity = default!;
        private float _lastGameDt;

        // Simple UI state machine
        private enum GameState { Initialization, MainMenu, Settings, Loading, Playing, Paused }
        private enum SettingsReturnState { MainMenu, Paused }
        private enum SettingsTab { General, Graphics, Keyboard, Debug }
        private enum KeyboardAction
        {
            Forward,
            Backward,
            Left,
            Right,
            Jump,
            DigInteract,
            DebugOverlay,
            Fullscreen,
            Hotbar1,
            Hotbar2,
            Hotbar3,
            Hotbar4,
            Hotbar5,
            Hotbar6,
            Hotbar7,
            Hotbar8,
            Hotbar9,
            Scroll
        }
        private GameState _state = GameState.MainMenu;
        private SettingsReturnState _settingsReturnState = SettingsReturnState.MainMenu;
        private SettingsTab _settingsTab = SettingsTab.General;
        private int _keyboardTabScrollOffset;
        private bool _isCapturingBinding;
        private KeyboardAction _capturingAction;
        private bool _capturingPrimary = true;
        private int _captureIgnoreFrames;

        // UI asset keys
        private const string LogoTextureKey = "ui/logo";
        private const string SplashTextureKey = "ui/splash";

        // Loading state
        private float _loadingProgress;
        private string _loadingStageText = "Preparing world";
        private int _loadingLoadedChunks;
        private int _loadingDesiredChunks;
        private int _loadingGeneratingChunks;
        private double _loadingCompleteTime;
        private bool _requestedExit;
        private Task _digTask = Task.CompletedTask;

        // Initialization splash timing
        private const double InitDurationSeconds = 0.75;

        public VeilborneEngine(
            ICameraController cameraController,
            IPhysicsController physics,
            IInfiniteTerrain terrain,
            IItemRegistry items,
            ITimeService time,
            EntityRegistry entities,
            IGraphicsProvider graphics,
            IGameLoopHost loopHost,
            IInputProvider input,
            IEcsRuntime ecsRuntime,
            IGameSettingsService settings)
        {
            _cameraController = cameraController;
            _physics = physics;
            _terrain = terrain;
            _items = items;
            _time = time;
            _entities = entities;
            _graphics = graphics;
            _loopHost = loopHost;
            _input = input;
            _ecsRuntime = ecsRuntime;
            _settings = settings;
            _isDevelopmentEnvironment = RuntimeEnvironment.IsDevelopmentEnvironment;
        }

        public Task RunAsync()
        {
            _graphics.InitializeWindow(1280, 720, "Veilborne");
            ApplySettings(initialApply: true);

            // Set up player entity
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

            _state = GameState.MainMenu;

            _loopHost.SetLoadContentCallback(() =>
            {
                _ecsRuntime.Initialize(_entities, _terrain);
                _ui = _ecsRuntime.GetUiProvider();
                _input.ShowCursor();
                LoadUiAssets();
            });
            _loopHost.SetUpdateCallback(UpdateStep);
            _loopHost.Set3DDrawCallback(Draw3DStep);
            _loopHost.Set2DDrawCallback(Draw2DStep);
            _loopHost.RunGameLoop();

            return Task.CompletedTask;
        }

        private void UpdateStep(float dt)
        {
            if (_requestedExit) { _graphics.CloseWindow(); return; }

            _input.UpdateStates();
            _time.Update(dt);
            float gameDt = _time.DeltaTime;
            _lastGameDt = gameDt;

            HandleInput(gameDt);

            if (_state == GameState.Playing)
            {
                _ecsRuntime.UpdateSystems(gameDt);
                // Process terrain async completions (RO/LOD chunk installs + spawned entities).
                if (_terrain is TerrainManager tm)
                    tm.PumpAsyncJobs().GetAwaiter().GetResult();
            }
            else if (_state == GameState.Initialization)
            {
                _state = GameState.MainMenu;
            }
            else if (_state == GameState.Loading)
            {
                var cam = _playerEntity.GetComponent<CameraComponent>();
                _terrain.UpdateCenter(cam.Position);
                if (_terrain is TerrainManager tm)
                {
                    tm.PumpAsyncJobs().GetAwaiter().GetResult();
                    var loading = tm.GetLoadingProgress();
                    _loadingProgress = loading.Progress01;
                    _loadingStageText = loading.Stage;
                    _loadingDesiredChunks = loading.DesiredChunks;
                    _loadingLoadedChunks = loading.LoadedChunks;
                    _loadingGeneratingChunks = loading.GeneratingChunks;
                }
                else
                {
                    _loadingProgress = 1f;
                    _loadingStageText = "Complete";
                    _loadingDesiredChunks = 0;
                    _loadingLoadedChunks = 0;
                    _loadingGeneratingChunks = 0;
                }

                if (_loadingProgress >= 0.999f && _loadingGeneratingChunks == 0)
                    _loadingCompleteTime += dt;
                else
                    _loadingCompleteTime = 0;

                if (_loadingCompleteTime >= 0.2)
                {
                    _state = GameState.Playing;
                    _loadingCompleteTime = 0;
                    _loadingProgress = 0;
                    _input.HideCursor();
                }
            }
        }

        private void Draw3DStep()
        {
            if (_state == GameState.Playing || _state == GameState.Paused)
            {
                var camera = _playerEntity.GetComponent<CameraComponent>();
                _graphics.Begin3D(camera);
                _ecsRuntime.RenderSystems(_lastGameDt, camera);
                if (_showDebugChunkBounds) _terrain.RenderDebugChunkBounds(camera);
                _graphics.End3D();
            }
        }

        private void Draw2DStep()
        {
            _time.NotifyFrameRendered();
            switch (_state)
            {
                case GameState.Initialization: DrawInitializationScreen(); break;
                case GameState.MainMenu: DrawMainMenu(); break;
                case GameState.Settings: DrawSettingsMenu(); break;
                case GameState.Loading: DrawLoadingScreen(); break;
                case GameState.Playing:
                    if (_showDebugOverlay) DrawDebugOverlay();
                    if (_settings.Current.General.ShowCrosshair) DrawCrosshair();
                    DrawHotbar();
                    break;
                case GameState.Paused:
                    DrawPauseOverlay();
                    DrawPauseMenu();
                    break;
            }
        }

        private void HandleInput(float dt)
        {
            if (_captureIgnoreFrames > 0)
                _captureIgnoreFrames--;

            if (_state == GameState.Settings && _isCapturingBinding)
            {
                HandleBindingCaptureInput();
                return;
            }

            var keyboard = _settings.Current.Keyboard;

            // Global toggles (available in all states)
            if (_isDevelopmentEnvironment)
            {
                if (KeyBindingTokens.IsPressed(_input, keyboard.DebugOverlay)) _showDebugOverlay = !_showDebugOverlay;
                if (_input.IsKeyPressed(InputKeys.KEY_F2)) _showDebugChunkBounds = !_showDebugChunkBounds;
                if (KeyBindingTokens.IsPressed(_input, keyboard.DebugOverlay))
                    _settings.Update(s => s.Debug.ShowDebugOverlay = _showDebugOverlay);
                if (_input.IsKeyPressed(InputKeys.KEY_F2))
                    _settings.Update(s => s.Debug.ShowChunkBounds = _showDebugChunkBounds);
            }

            // Toggle fullscreen on bound key press
            if (KeyBindingTokens.IsPressed(_input, keyboard.Fullscreen))
            {
                _settings.Update(s => s.Graphics.Fullscreen = !s.Graphics.Fullscreen);
                ApplySettings();
            }

            switch (_state)
            {
                case GameState.MainMenu:
                    if (_input.IsKeyPressed(InputKeys.KEY_ENTER))
                        StartGame();
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                        _requestedExit = true;
                    break;

                case GameState.Loading:
                    // Allow cancel back to menu if needed
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        _state = GameState.MainMenu;
                        _loadingCompleteTime = 0;
                        _loadingProgress = 0;
                        _loadingStageText = "Preparing world";
                        _loadingLoadedChunks = 0;
                        _loadingDesiredChunks = 0;
                        _loadingGeneratingChunks = 0;
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
                    if (wheel != 0 && IsBindingConfigured(keyboard.Scroll))
                    {
                        int delta = wheel > 0 ? -1 : 1;
                        _selectedHotbarSlot = ((_selectedHotbarSlot + delta) % 9 + 9) % 9;
                    }
                    else if (KeyBindingTokens.IsPressed(_input, keyboard.Scroll))
                    {
                        _selectedHotbarSlot = (_selectedHotbarSlot + 1) % 9;
                    }

                    // Hotbar selection 1-9
                    for (int i = 0; i < 9; i++)
                    {
                        if (KeyBindingTokens.IsPressed(_input, GetHotbarBinding(i)))
                            _selectedHotbarSlot = i;
                    }

                    // Example dig action
                    if (KeyBindingTokens.IsPressed(_input, keyboard.DigInteract))
                    {
                        if (_terrain is IEditableTerrain editable && _digTask.IsCompleted)
                        {
                            // Only dig if we are looking at the ground in front of us
                            if (TryGetGroundHit(6f, 0.25f, 0.05f, out var hit))
                            {
                                _digTask = editable.DigSphereAsync(hit, 1f, 1f, VoxelFalloff.Linear);
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

                case GameState.Settings:
                    if (_settingsTab == SettingsTab.Keyboard && !_isCapturingBinding)
                        HandleKeyboardTabScrollInput();
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        ReturnFromSettings();
                    }
                    break;
            }
        }

        private void LoadUiAssets()
        {
            _ui.RegisterSvgTexture(SplashTextureKey, "assets\\splash.svg", 2000, 1200);
            _graphics.SetWindowIcon("assets\\logo.svg");
        }

        private void StartGame()
        {
            var cam = _playerEntity.GetComponent<CameraComponent>();
            cam.Position = new Vector3(0, 5, -10);
            cam.Target = Vector3.Zero;

            // Begin loading/warmup
            _state = GameState.Loading;
            _loadingProgress = 0;
            _loadingStageText = "Preparing world";
            _loadingLoadedChunks = 0;
            _loadingDesiredChunks = 0;
            _loadingGeneratingChunks = 0;
            _loadingCompleteTime = 0;
        }

        private void DrawMainMenu()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;

            _graphics.Clear(new Vector3(15 / 255f, 18 / 255f, 22 / 255f));

            // Buttons
            int btnW = Math.Min(340, (int)(w * 0.35f));
            int btnH = 52;
            int xCenter = w / 2 - btnW / 2;
            int startY = (int)(h * 0.62f);
            int gap = btnH + 14;

            bool drewSplash = false;
            if (_ui.HasTexture(SplashTextureKey) &&
                _ui.TryGetTextureSize(SplashTextureKey, out int texW, out int texH))
            {
                int maxW = Math.Min((int)(w * 0.99f), 2200);
                int maxH = Math.Min((int)(h * 0.58f), 860);
                float scale = MathF.Min(maxW / (float)texW, maxH / (float)texH);
                int drawW = Math.Max(1, (int)MathF.Round(texW * scale));
                int drawH = Math.Max(1, (int)MathF.Round(texH * scale));
                int x = w / 2 - drawW / 2;
                int y = Math.Max(0, startY - drawH - 6);
                _ui.DrawTexture(SplashTextureKey, x, y, scale, Vector4.One);
                drewSplash = true;
            }

            if (!drewSplash)
            {
                int titleSize = 64;
                string title = "VEILBORNE";
                int tw = _ui.MeasureText(title, titleSize);
                _ui.DrawText(title, w / 2 - tw / 2, (int)(h * 0.28f), titleSize, Vector4.One);
            }

            if (Button("Play", new Rect(xCenter, startY, btnW, btnH)))
                StartGame();

            if (Button("Settings", new Rect(xCenter, startY + gap, btnW, btnH)))
            {
                OpenSettings(SettingsReturnState.MainMenu);
                return;
            }

            if (Button("Exit", new Rect(xCenter, startY + gap * 2, btnW, btnH)))
                _requestedExit = true;
        }

        private void DrawLoadingScreen()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _graphics.Clear(new Vector3(10 / 255f, 12 / 255f, 16 / 255f));

            string title = "Loading world...";
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

            string progressText = $"{Math.Clamp((int)(_loadingProgress * 100f), 0, 100)}%";
            string stageText = string.IsNullOrWhiteSpace(_loadingStageText) ? "Preparing world" : _loadingStageText;
            int stw = _ui.MeasureText(stageText, 20);
            _ui.DrawText(stageText, w / 2 - stw / 2, y + barH + 10, 20, Vector4.One);

            string chunkText = _loadingDesiredChunks > 0
                ? $"Chunks: {_loadingLoadedChunks}/{_loadingDesiredChunks}" + (_loadingGeneratingChunks > 0 ? $" (generating {_loadingGeneratingChunks})" : "")
                : "Chunks: preparing";
            int ctw = _ui.MeasureText(chunkText, 18);
            _ui.DrawText(chunkText, w / 2 - ctw / 2, y + barH + 36, 18, new Vector4(0.82f, 0.88f, 0.95f, 1f));

            int ptw = _ui.MeasureText(progressText, 20);
            _ui.DrawText(progressText, w / 2 - ptw / 2, y - 30, 20, Vector4.One);
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
            float p = 1f;
            string stage = "Complete";

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
            if (Button("Settings", new Rect(xCenter, startY + btnH + 14, btnW, btnH)))
            {
                OpenSettings(SettingsReturnState.Paused);
                return;
            }
            if (Button("Exit to Menu", new Rect(xCenter, startY + (btnH + 14) * 2, btnW, btnH)))
            {
                _state = GameState.MainMenu;
                _input.ShowCursor();
                return;
            }
            if (Button("Exit to Desktop", new Rect(xCenter, startY + (btnH + 14) * 3, btnW, btnH)))
            {
                _requestedExit = true;
                return;
            }
        }

        private void DrawSettingsMenu()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _graphics.Clear(new Vector3(15 / 255f, 18 / 255f, 22 / 255f));

            int panelW = Math.Min(900, (int)(w * 0.8f));
            int panelH = Math.Min(540, (int)(h * 0.75f));
            int panelX = w / 2 - panelW / 2;
            int panelY = h / 2 - panelH / 2;

            _ui.DrawRectangle(panelX, panelY, panelW, panelH, new Vector4(28 / 255f, 34 / 255f, 42 / 255f, 1f));
            _ui.DrawRectangleLines(panelX, panelY, panelW, panelH, new Vector4(90 / 255f, 100 / 255f, 115 / 255f, 1f));

            string title = "Settings";
            _ui.DrawText(title, panelX + 24, panelY + 18, 34, Vector4.One);

            int tabY = panelY + 72;
            int tabW = 130;
            int tabH = 42;
            int tabGap = 10;
            if (Button("General", new Rect(panelX + 24, tabY, tabW, tabH), 22)) _settingsTab = SettingsTab.General;
            if (Button("Graphics", new Rect(panelX + 24 + (tabW + tabGap), tabY, tabW, tabH), 22)) _settingsTab = SettingsTab.Graphics;
            if (Button("Keyboard", new Rect(panelX + 24 + (tabW + tabGap) * 2, tabY, tabW, tabH), 22)) _settingsTab = SettingsTab.Keyboard;
            if (_isDevelopmentEnvironment &&
                Button("Debug", new Rect(panelX + 24 + (tabW + tabGap) * 3, tabY, tabW, tabH), 22))
                _settingsTab = SettingsTab.Debug;
            if (!_isDevelopmentEnvironment && _settingsTab == SettingsTab.Debug)
                _settingsTab = SettingsTab.General;

            int contentX = panelX + 30;
            int contentY = tabY + tabH + 18;
            int lineH = 40;

            var settings = _settings.Current;
            switch (_settingsTab)
            {
                case SettingsTab.General:
                {
                    _ui.DrawText("General", contentX, contentY, 28, Vector4.One);
                    contentY += 46;
                    DrawLabeledOption("Mouse Sensitivity", $"{settings.General.MouseSensitivity:0.0000}", contentX, contentY);
                    if (Button("-", new Rect(contentX + 360, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.General.MouseSensitivity -= 0.0005f);
                        ApplySettings();
                    }
                    if (Button("+", new Rect(contentX + 410, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.General.MouseSensitivity += 0.0005f);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Invert Mouse Y", settings.General.InvertMouseY ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.General.InvertMouseY = !s.General.InvertMouseY);
                    }
                    contentY += lineH;

                    DrawLabeledOption("Show Crosshair", settings.General.ShowCrosshair ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.General.ShowCrosshair = !s.General.ShowCrosshair);
                    }
                    break;
                }
                case SettingsTab.Graphics:
                {
                    _ui.DrawText("Graphics", contentX, contentY, 28, Vector4.One);
                    contentY += 46;

                    DrawLabeledOption("Target FPS", $"{settings.Graphics.TargetFps}", contentX, contentY);
                    if (Button("-", new Rect(contentX + 360, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Graphics.TargetFps = Math.Max(30, s.Graphics.TargetFps - 10));
                        ApplySettings();
                    }
                    if (Button("+", new Rect(contentX + 410, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Graphics.TargetFps = Math.Min(240, s.Graphics.TargetFps + 10));
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Fullscreen", settings.Graphics.Fullscreen ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Graphics.Fullscreen = !s.Graphics.Fullscreen);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Render Distance", $"{settings.Graphics.RenderDistance}%", contentX, contentY);
                    if (Button("-", new Rect(contentX + 360, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Graphics.RenderDistance = Math.Max(40, s.Graphics.RenderDistance - 10));
                        ApplySettings();
                    }
                    if (Button("+", new Rect(contentX + 410, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Graphics.RenderDistance = Math.Min(200, s.Graphics.RenderDistance + 10));
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Brightness", $"{settings.Graphics.Brightness}%", contentX, contentY);
                    if (Button("-", new Rect(contentX + 360, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Graphics.Brightness = Math.Max(50, s.Graphics.Brightness - 5));
                        ApplySettings();
                    }
                    if (Button("+", new Rect(contentX + 410, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Graphics.Brightness = Math.Min(150, s.Graphics.Brightness + 5));
                        ApplySettings();
                    }
                    break;
                }
                case SettingsTab.Debug:
                {
                    _ui.DrawText("Debug", contentX, contentY, 28, Vector4.One);
                    contentY += 46;

                    DrawLabeledOption("Debug Overlay (F1)", settings.Debug.ShowDebugOverlay ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Debug.ShowDebugOverlay = !s.Debug.ShowDebugOverlay);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Chunk Bounds (F2)", settings.Debug.ShowChunkBounds ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Debug.ShowChunkBounds = !s.Debug.ShowChunkBounds);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Wireframe", settings.Debug.Wireframe ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Debug.Wireframe = !s.Debug.Wireframe);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Show Editable Ring", settings.Debug.ShowEditableRing ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Debug.ShowEditableRing = !s.Debug.ShowEditableRing);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Show ReadOnly Ring", settings.Debug.ShowReadOnlyRing ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Debug.ShowReadOnlyRing = !s.Debug.ShowReadOnlyRing);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Show LowLod Ring", settings.Debug.ShowLowLodRing ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Debug.ShowLowLodRing = !s.Debug.ShowLowLodRing);
                        ApplySettings();
                    }
                    contentY += lineH;

                    DrawLabeledOption("Run Speed Multiplier", $"{settings.Debug.RunSpeedMultiplier}%", contentX, contentY);
                    if (Button("-", new Rect(contentX + 360, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Debug.RunSpeedMultiplier = Math.Max(50, s.Debug.RunSpeedMultiplier - 10));
                        ApplySettings();
                    }
                    if (Button("+", new Rect(contentX + 410, contentY - 4, 42, 32), 22))
                    {
                        _settings.Update(s => s.Debug.RunSpeedMultiplier = Math.Min(300, s.Debug.RunSpeedMultiplier + 10));
                        ApplySettings();
                    }
                    break;
                }
                case SettingsTab.Keyboard:
                {
                    DrawKeyboardSettingsPanel(panelX, panelY, panelW, panelH, contentX, contentY, lineH);
                    break;
                }
            }

            if (Button("Back", new Rect(panelX + panelW - 170, panelY + panelH - 62, 140, 40), 22))
            {
                if (_isCapturingBinding)
                {
                    _isCapturingBinding = false;
                    _captureIgnoreFrames = 2;
                    return;
                }
                ReturnFromSettings();
            }
        }

        private void DrawLabeledOption(string label, string value, int x, int y)
        {
            _ui.DrawText(label, x, y, 22, Vector4.One);
            _ui.DrawText(value, x + 250, y, 22, new Vector4(0.75f, 0.9f, 1f, 1f));
        }

        private void DrawKeyboardSettingsPanel(int panelX, int panelY, int panelW, int panelH, int contentX, int contentY, int lineH)
        {
            _ui.DrawText("Keyboard", contentX, contentY, 28, Vector4.One);
            int contentTop = contentY + 46;
            int footerHeight = _isCapturingBinding ? 74 : 46;
            int usableBottom = panelY + panelH - 62 - footerHeight;
            int visibleRows = Math.Max(1, (usableBottom - contentTop) / lineH);

            var bindings = GetKeyboardActionRows();
            int totalRows = bindings.Length;
            int availableWidth = panelW - 60;
            int columnWidth = Math.Min(540, Math.Max(300, availableWidth));
            int maxOffset = Math.Max(0, totalRows - visibleRows);
            _keyboardTabScrollOffset = Math.Clamp(_keyboardTabScrollOffset, 0, maxOffset);
            int startIndex = _keyboardTabScrollOffset;
            int endExclusive = Math.Min(totalRows, startIndex + visibleRows);

            for (int i = startIndex; i < endExclusive; i++)
            {
                int row = i - startIndex;
                int rowX = contentX;
                int rowY = contentTop + row * lineH;
                var item = bindings[i];
                DrawKeyboardBindingRow(item.label, item.action, rowX, rowY, columnWidth, item.disabled);
            }

            if (maxOffset > 0)
            {
                int barX = contentX + columnWidth + 6;
                int barY = contentTop;
                int barH = visibleRows * lineH - 8;
                _ui.DrawRectangleLines(barX, barY, 10, barH, new Vector4(0.4f, 0.45f, 0.5f, 1f));
                float thumbRatio = visibleRows / (float)totalRows;
                int thumbH = Math.Max(18, (int)(barH * thumbRatio));
                int thumbTravel = Math.Max(0, barH - thumbH);
                int thumbY = barY + (thumbTravel == 0 ? 0 : (int)(thumbTravel * (_keyboardTabScrollOffset / (float)maxOffset)));
                _ui.DrawRectangle(barX + 1, thumbY + 1, 8, Math.Max(1, thumbH - 2), new Vector4(0.55f, 0.75f, 0.95f, 1f));
                _ui.DrawText($"{startIndex + 1}-{endExclusive} / {totalRows}", contentX, panelY + panelH - 106, 18, new Vector4(0.75f, 0.9f, 1f, 1f));
            }

            if (_isCapturingBinding)
            {
                string captureText = $"Press a key/button for {GetActionLabel(_capturingAction)} ({(_capturingPrimary ? "Primary" : "Secondary")})...";
                _ui.DrawText(captureText, contentX, panelY + panelH - 92, 18, new Vector4(1f, 0.85f, 0.35f, 1f));
                _ui.DrawText("Esc cancel | Backspace/Delete clear", contentX, panelY + panelH - 70, 18, new Vector4(0.8f, 0.85f, 0.9f, 1f));
            }
        }

        private void HandleKeyboardTabScrollInput()
        {
            int deltaRows = 0;
            float wheel = _input.GetMouseWheelMove();
            if (wheel > 0) deltaRows -= 1;
            if (wheel < 0) deltaRows += 1;
            if (_input.IsKeyPressed(InputKeys.KEY_UP)) deltaRows -= 1;
            if (_input.IsKeyPressed(InputKeys.KEY_DOWN)) deltaRows += 1;

            if (deltaRows != 0)
            {
                _keyboardTabScrollOffset = Math.Max(0, _keyboardTabScrollOffset + deltaRows);
            }
        }

        private (string label, KeyboardAction action, bool disabled)[] GetKeyboardActionRows()
        {
            return new (string label, KeyboardAction action, bool disabled)[]
            {
                ("Forward", KeyboardAction.Forward, false),
                ("Backward", KeyboardAction.Backward, false),
                ("Left", KeyboardAction.Left, false),
                ("Right", KeyboardAction.Right, false),
                ("Jump", KeyboardAction.Jump, false),
                ("Dig / Interact", KeyboardAction.DigInteract, false),
                ("Debug Overlay", KeyboardAction.DebugOverlay, !_isDevelopmentEnvironment),
                ("Fullscreen", KeyboardAction.Fullscreen, false),
                ("Hotbar 1", KeyboardAction.Hotbar1, false),
                ("Hotbar 2", KeyboardAction.Hotbar2, false),
                ("Hotbar 3", KeyboardAction.Hotbar3, false),
                ("Hotbar 4", KeyboardAction.Hotbar4, false),
                ("Hotbar 5", KeyboardAction.Hotbar5, false),
                ("Hotbar 6", KeyboardAction.Hotbar6, false),
                ("Hotbar 7", KeyboardAction.Hotbar7, false),
                ("Hotbar 8", KeyboardAction.Hotbar8, false),
                ("Hotbar 9", KeyboardAction.Hotbar9, false),
                ("Scroll", KeyboardAction.Scroll, false),
            };
        }

        private void DrawKeyboardBindingRow(string label, KeyboardAction action, int x, int y, int rowWidth, bool disabled = false)
        {
            var binding = GetBinding(action);
            string primaryText = KeyBindingTokens.ToDisplay(binding.Primary);
            string secondaryText = KeyBindingTokens.ToDisplay(binding.Secondary);
            int primaryW = rowWidth >= 500 ? 88 : 70;
            int secondaryW = rowWidth >= 500 ? 104 : 86;
            int buttonGap = 8;
            int secondaryX = x + rowWidth - secondaryW;
            int primaryX = secondaryX - buttonGap - primaryW;
            int valueX = x + Math.Clamp(rowWidth / 3, 130, 200);
            string primaryLabel = rowWidth >= 500 ? "Primary" : "P";
            string secondaryLabel = rowWidth >= 500 ? "Secondary" : "S";
            _ui.DrawText(label, x, y, 20, Vector4.One);
            _ui.DrawText($"{primaryText} / {secondaryText}", valueX, y, 20, new Vector4(0.75f, 0.9f, 1f, 1f));

            if (Button(primaryLabel, new Rect(primaryX, y - 4, primaryW, 32), 16) && !disabled)
            {
                _isCapturingBinding = true;
                _capturingAction = action;
                _capturingPrimary = true;
                _captureIgnoreFrames = 2;
            }
            if (Button(secondaryLabel, new Rect(secondaryX, y - 4, secondaryW, 32), 16) && !disabled)
            {
                _isCapturingBinding = true;
                _capturingAction = action;
                _capturingPrimary = false;
                _captureIgnoreFrames = 2;
            }
            if (disabled)
                _ui.DrawText("Dev only", secondaryX - 90, y + 2, 16, new Vector4(0.9f, 0.6f, 0.35f, 1f));
        }

        private void HandleBindingCaptureInput()
        {
            if (_captureIgnoreFrames > 0)
                return;

            if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
            {
                _isCapturingBinding = false;
                _captureIgnoreFrames = 2;
                return;
            }

            if (_input.IsKeyPressed(InputKeys.KEY_BACKSPACE) || _input.IsKeyPressed(InputKeys.KEY_DELETE))
            {
                SetBindingToken(_capturingAction, _capturingPrimary, KeyBindingTokens.None);
                _isCapturingBinding = false;
                _captureIgnoreFrames = 2;
                return;
            }

            var pressedKeys = _input.GetPressedKeys();
            foreach (int key in pressedKeys)
            {
                string token = KeyBindingTokens.FromKeyCode(key);
                if (token == KeyBindingTokens.None)
                    continue;
                if (!_isDevelopmentEnvironment && _capturingAction == KeyboardAction.DebugOverlay)
                    break;
                SetBindingToken(_capturingAction, _capturingPrimary, token);
                _isCapturingBinding = false;
                _captureIgnoreFrames = 2;
                return;
            }

            var pressedMouseButtons = _input.GetPressedMouseButtons();
            foreach (int button in pressedMouseButtons)
            {
                string token = KeyBindingTokens.FromMouseButton(button);
                if (token == KeyBindingTokens.None)
                    continue;
                if (!_isDevelopmentEnvironment && _capturingAction == KeyboardAction.DebugOverlay)
                    break;
                SetBindingToken(_capturingAction, _capturingPrimary, token);
                _isCapturingBinding = false;
                _captureIgnoreFrames = 2;
                return;
            }
        }

        private InputBindingSettings GetBinding(KeyboardAction action)
        {
            var keyboard = _settings.Current.Keyboard;
            return action switch
            {
                KeyboardAction.Forward => keyboard.Forward,
                KeyboardAction.Backward => keyboard.Backward,
                KeyboardAction.Left => keyboard.Left,
                KeyboardAction.Right => keyboard.Right,
                KeyboardAction.Jump => keyboard.Jump,
                KeyboardAction.DigInteract => keyboard.DigInteract,
                KeyboardAction.DebugOverlay => keyboard.DebugOverlay,
                KeyboardAction.Fullscreen => keyboard.Fullscreen,
                KeyboardAction.Hotbar1 => keyboard.Hotbar1,
                KeyboardAction.Hotbar2 => keyboard.Hotbar2,
                KeyboardAction.Hotbar3 => keyboard.Hotbar3,
                KeyboardAction.Hotbar4 => keyboard.Hotbar4,
                KeyboardAction.Hotbar5 => keyboard.Hotbar5,
                KeyboardAction.Hotbar6 => keyboard.Hotbar6,
                KeyboardAction.Hotbar7 => keyboard.Hotbar7,
                KeyboardAction.Hotbar8 => keyboard.Hotbar8,
                KeyboardAction.Hotbar9 => keyboard.Hotbar9,
                KeyboardAction.Scroll => keyboard.Scroll,
                _ => keyboard.Forward
            };
        }

        private string GetActionLabel(KeyboardAction action)
        {
            return action switch
            {
                KeyboardAction.Forward => "Forward",
                KeyboardAction.Backward => "Backward",
                KeyboardAction.Left => "Left",
                KeyboardAction.Right => "Right",
                KeyboardAction.Jump => "Jump",
                KeyboardAction.DigInteract => "Dig / Interact",
                KeyboardAction.DebugOverlay => "Debug Overlay",
                KeyboardAction.Fullscreen => "Fullscreen",
                KeyboardAction.Hotbar1 => "Hotbar 1",
                KeyboardAction.Hotbar2 => "Hotbar 2",
                KeyboardAction.Hotbar3 => "Hotbar 3",
                KeyboardAction.Hotbar4 => "Hotbar 4",
                KeyboardAction.Hotbar5 => "Hotbar 5",
                KeyboardAction.Hotbar6 => "Hotbar 6",
                KeyboardAction.Hotbar7 => "Hotbar 7",
                KeyboardAction.Hotbar8 => "Hotbar 8",
                KeyboardAction.Hotbar9 => "Hotbar 9",
                KeyboardAction.Scroll => "Scroll",
                _ => action.ToString()
            };
        }

        private void SetBindingToken(KeyboardAction action, bool primary, string token)
        {
            string normalized = KeyBindingTokens.Normalize(token);
            _settings.Update(s =>
            {
                var binding = action switch
                {
                    KeyboardAction.Forward => s.Keyboard.Forward,
                    KeyboardAction.Backward => s.Keyboard.Backward,
                    KeyboardAction.Left => s.Keyboard.Left,
                    KeyboardAction.Right => s.Keyboard.Right,
                    KeyboardAction.Jump => s.Keyboard.Jump,
                    KeyboardAction.DigInteract => s.Keyboard.DigInteract,
                    KeyboardAction.DebugOverlay => s.Keyboard.DebugOverlay,
                    KeyboardAction.Fullscreen => s.Keyboard.Fullscreen,
                    KeyboardAction.Hotbar1 => s.Keyboard.Hotbar1,
                    KeyboardAction.Hotbar2 => s.Keyboard.Hotbar2,
                    KeyboardAction.Hotbar3 => s.Keyboard.Hotbar3,
                    KeyboardAction.Hotbar4 => s.Keyboard.Hotbar4,
                    KeyboardAction.Hotbar5 => s.Keyboard.Hotbar5,
                    KeyboardAction.Hotbar6 => s.Keyboard.Hotbar6,
                    KeyboardAction.Hotbar7 => s.Keyboard.Hotbar7,
                    KeyboardAction.Hotbar8 => s.Keyboard.Hotbar8,
                    KeyboardAction.Hotbar9 => s.Keyboard.Hotbar9,
                    KeyboardAction.Scroll => s.Keyboard.Scroll,
                    _ => s.Keyboard.Forward
                };
                if (primary) binding.Primary = normalized;
                else binding.Secondary = normalized;
            });
        }

        private InputBindingSettings GetHotbarBinding(int index)
        {
            var keyboard = _settings.Current.Keyboard;
            return index switch
            {
                0 => keyboard.Hotbar1,
                1 => keyboard.Hotbar2,
                2 => keyboard.Hotbar3,
                3 => keyboard.Hotbar4,
                4 => keyboard.Hotbar5,
                5 => keyboard.Hotbar6,
                6 => keyboard.Hotbar7,
                7 => keyboard.Hotbar8,
                8 => keyboard.Hotbar9,
                _ => keyboard.Hotbar1
            };
        }

        private static bool IsBindingConfigured(InputBindingSettings binding)
        {
            return KeyBindingTokens.Normalize(binding.Primary) != KeyBindingTokens.None ||
                   KeyBindingTokens.Normalize(binding.Secondary) != KeyBindingTokens.None;
        }

        private void OpenSettings(SettingsReturnState returnState)
        {
            _settingsReturnState = returnState;
            _state = GameState.Settings;
            _keyboardTabScrollOffset = 0;
            _isCapturingBinding = false;
            _input.ShowCursor();
        }

        private void ReturnFromSettings()
        {
            _state = _settingsReturnState == SettingsReturnState.Paused ? GameState.Paused : GameState.MainMenu;
            if (_state == GameState.Playing) _input.HideCursor();
            else _input.ShowCursor();
        }

        private void ApplySettings(bool initialApply = false)
        {
            var settings = _settings.Current;
            _graphics.SetTargetFps(settings.Graphics.TargetFps);

            if (_isDevelopmentEnvironment)
            {
                _showDebugOverlay = settings.Debug.ShowDebugOverlay;
                _showDebugChunkBounds = settings.Debug.ShowChunkBounds;
            }
            else
            {
                _showDebugOverlay = false;
                _showDebugChunkBounds = false;
            }

            if (!initialApply)
            {
                bool shouldBeFullscreen = settings.Graphics.Fullscreen;
                if (shouldBeFullscreen != _isFullscreenApplied)
                {
                    _graphics.ToggleFullscreen();
                    _isFullscreenApplied = shouldBeFullscreen;
                }
            }
            else
            {
                _isFullscreenApplied = false;
                if (settings.Graphics.Fullscreen)
                {
                    _graphics.ToggleFullscreen();
                    _isFullscreenApplied = true;
                }
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
