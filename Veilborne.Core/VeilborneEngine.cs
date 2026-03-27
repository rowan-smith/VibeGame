using System.Numerics;
using Veilborne.Interfaces;
using Veilborne.Terrain;
using Veilborne.Core.Ecs;
using Veilborne.Core.Ecs.Components;
using Veilborne.Core.Settings;
using Veilborne.Core.Sky;
using Veilborne.Core.UI;
using Serilog;

namespace Veilborne.Core
{
    public class VeilborneEngine : IGameEngine
    {
        private readonly IInfiniteTerrain _terrain;
        private readonly ITimeService _time;
        private readonly EntityRegistry _entities;
        private readonly IGraphicsProvider _graphics;
        private readonly IGameLoopHost _loopHost;
        private readonly IInputProvider _input;
        private readonly IEcsRuntime _ecsRuntime;
        private readonly IGameSettingsService _settings;
        private readonly ISkyLightingService _sky;
        private readonly HudUiController _hudUi;
        private readonly DebugOverlayUiController _debugOverlayUi;
        private readonly bool _isDevelopmentEnvironment;
        private readonly ILogger _log = Log.ForContext<VeilborneEngine>();

        // These will be initialized by ECS manager after MonoGame is ready
        private IUiProvider _ui;

        private bool _showDebugOverlay;
        private bool _showDebugChunkBounds;
        private bool _showColliderRadii;
        private bool _isFullscreenApplied;
        private Entity _playerEntity = default!;
        private Entity _uiCanvasEntity = default!;
        private Entity _crosshairEntity = default!;
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
        private int _loadingEntities;
        private double _loadingCompleteTime;
        private bool _requestedExit;
        private GameState _lastLoggedState = (GameState)(-1);
        private bool _uiLeftReleaseThisFrame;
        private bool _uiLeftReleaseConsumed;

        // Initialization splash timing
        private const double InitDurationSeconds = 0.75;

        public VeilborneEngine(
            IInfiniteTerrain terrain,
            ITimeService time,
            EntityRegistry entities,
            IGraphicsProvider graphics,
            IGameLoopHost loopHost,
            IInputProvider input,
            IEcsRuntime ecsRuntime,
            IGameSettingsService settings,
            ISkyLightingService sky,
            HudUiController hudUi,
            DebugOverlayUiController debugOverlayUi)
        {
            _terrain = terrain;
            _time = time;
            _entities = entities;
            _graphics = graphics;
            _loopHost = loopHost;
            _input = input;
            _ecsRuntime = ecsRuntime;
            _settings = settings;
            _sky = sky;
            _hudUi = hudUi;
            _debugOverlayUi = debugOverlayUi;
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
            _playerEntity.AddComponent(new ColliderComponent { Radius = 0.5f });
            _playerEntity.AddComponent(new CollisionFilterComponent
            {
                Layer = CollisionLayer.Player,
                CollidesWith = CollisionLayer.WorldStatic
            });
            _playerEntity.AddComponent(new VelocityComponent { Linear = Vector3.Zero });
            _playerEntity.AddComponent(new VerticalVelocityComponent { Value = 0f });
            _playerEntity.AddComponent(new AccelerationComponent { Value = Vector3.Zero });
            _playerEntity.AddComponent(new ForceComponent { Value = Vector3.Zero });
            _playerEntity.AddComponent(new DragComponent { Linear = 0f, Angular = 0f });
            _playerEntity.AddComponent(new MassComponent { Value = 1f, IsKinematic = false });
            _playerEntity.AddComponent(new RigidbodyComponent { IsKinematic = false, IsSleeping = false });
            _playerEntity.AddComponent(new GravityComponent { Direction = new Vector3(0f, -20f, 0f) });
            _playerEntity.AddComponent(new HealthComponent { Current = 100f, Max = 100f });
            _playerEntity.AddComponent(new TeamComponent { Id = 1 });
            _playerEntity.AddComponent(new NameComponent { Value = "Player" });
            _playerEntity.AddComponent(new TagComponent { Name = "Player" });
            _playerEntity.AddComponent(new ParentComponent { EntityId = -1 });
            _playerEntity.AddComponent(new ChildrenComponent { EntityIds = [] });
            _playerEntity.AddComponent(new LifetimeComponent { RemainingSeconds = 0f });
            _playerEntity.AddComponent(new DirtyComponent { NeedsUpdate = false });
            _playerEntity.AddComponent(new BillboardComponent { FaceCamera = false });
            _playerEntity.AddComponent(new ShadowCasterComponent { CastsShadows = true });
            _playerEntity.AddComponent(new MaterialComponent { ShaderId = string.Empty, Tint = Vector4.One });
            _playerEntity.AddComponent(new JumpComponent
            {
                JumpSpeed = 8.5f,
                JumpBufferSeconds = 0.12f,
                CoyoteSeconds = 0.10f,
                JumpBufferTimer = 0f,
                CoyoteTimer = 0f,
                IsGrounded = false
            });
            _playerEntity.AddComponent(new MoveInputComponent { HorizontalDisplacement = Vector3.Zero });
            _playerEntity.AddComponent(new HotbarSelectionComponent { SelectedSlot = 0 });
            _playerEntity.AddComponent(new DigInteractionComponent
            {
                IsDigHeld = false,
                HasGroundHit = false,
                GroundHit = Vector3.Zero,
                ProbeMaxDistance = 6f,
                ProbeStep = 0.25f,
                ProbeEpsilon = 0.05f,
                ToolBreakSpeedMultiplier = 1f,
                ToolStaminaCost = 0
            });
            _playerEntity.AddComponent(new MiningHitComponent
            {
                HasHit = false,
                HitPosition = Vector3.Zero,
                BlockType = Terrain.ResourceBlockType.None
            });
            var cameraComp = new CameraComponent
            {
                Position = transform.Position,
                Target = Vector3.Zero,
                Up = Vector3.UnitY,
                FovY = 45.0f
            };
            _playerEntity.AddComponent(cameraComp);

            // ECS UI canvas + crosshair element
            _uiCanvasEntity = _entities.CreateEntity();
            _uiCanvasEntity.AddComponent(new CanvasComponent
            {
                TargetCameraEntityId = _playerEntity.Id,
                Visible = true
            });

            _crosshairEntity = _entities.CreateEntity();
            _crosshairEntity.AddComponent(new UIElementKindComponent { Kind = "Crosshair" });
            _crosshairEntity.AddComponent(new UIElementComponent
            {
                Bounds = new Rect(0, 0, 0, 0),
                Text = "idle"
            });
            _crosshairEntity.AddComponent(new RenderComponent
            {
                Visible = true,
                ModelPath = string.Empty
            });

            _state = GameState.MainMenu;
            LogBindings();

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
            _sky.Update(dt);
            _graphics.SetSkyClearColor(_sky.SkyColor);
            float gameDt = _time.DeltaTime;
            _lastGameDt = gameDt;

            HandleInput(gameDt);
            LogStateTransition();

            if (_state == GameState.Playing)
            {
                _ecsRuntime.UpdateSystems(gameDt);
            }
            else if (_state == GameState.Initialization)
            {
                _state = GameState.MainMenu;
            }
            else if (_state == GameState.Loading)
            {
                var cam = _playerEntity.GetComponent<CameraComponent>();
                if (_terrain is TerrainManager tm)
                {
                    tm.SetWarmupMode(true);
                    tm.UpdateAround(cam.Position, 0);
                    tm.PumpAsyncJobs().GetAwaiter().GetResult();
                    var loading = tm.GetLoadingProgress();
                    _loadingProgress = loading.Progress01;
                    _loadingStageText = loading.Stage;
                    _loadingDesiredChunks = loading.DesiredChunks;
                    _loadingLoadedChunks = loading.LoadedChunks;
                    _loadingGeneratingChunks = loading.GeneratingChunks;
                    _loadingEntities = loading.LoadedEntities;
                }
                else
                {
                    _loadingProgress = 1f;
                    _loadingStageText = "Complete";
                    _loadingDesiredChunks = 0;
                    _loadingLoadedChunks = 0;
                    _loadingGeneratingChunks = 0;
                    _loadingEntities = 0;
                }

                if (_loadingProgress >= 0.999f && _loadingGeneratingChunks == 0)
                    _loadingCompleteTime += dt;
                else
                    _loadingCompleteTime = 0;

                if (_loadingCompleteTime >= 0.2)
                {
                    if (_terrain is TerrainManager readyTm)
                        readyTm.SetWarmupMode(false);
                    _state = GameState.Playing;
                    _loadingCompleteTime = 0;
                    _loadingProgress = 0;
                    _input.HideCursor();
                    _log.Debug("Entered Playing: cursor hidden and mouse lock requested.");
                }

                _playerEntity.SetComponent(cam);
            }
        }

        private void Draw3DStep()
        {
            if (_state == GameState.Playing || _state == GameState.Paused)
            {
                var camera = _playerEntity.GetComponent<CameraComponent>();
                _graphics.Begin3D(camera);
                _ecsRuntime.RenderSystems(_lastGameDt, camera);
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
                    if (_showDebugChunkBounds) DrawChunkBoundsOverlay();
                    if (_showColliderRadii) DrawColliderRadiiOverlay();
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
            _uiLeftReleaseThisFrame = _input.IsMouseButtonReleased(InputKeys.MOUSE_BUTTON_LEFT);
            _uiLeftReleaseConsumed = false;

            if (_captureIgnoreFrames > 0)
                _captureIgnoreFrames--;

            if (_state == GameState.Settings && _isCapturingBinding)
            {
                HandleBindingCaptureInput();
                return;
            }

            var keyboard = _settings.Current.Keyboard;

            // Global toggles (available in all states)
            if (KeyBindingTokens.IsPressed(_input, keyboard.DebugOverlay)) _showDebugOverlay = !_showDebugOverlay;
            if (_input.IsKeyPressed(InputKeys.KEY_F2)) _showDebugChunkBounds = !_showDebugChunkBounds;
            if (_input.IsKeyPressed(InputKeys.KEY_F3)) _showColliderRadii = !_showColliderRadii;
            if (KeyBindingTokens.IsPressed(_input, keyboard.DebugOverlay))
                _settings.Update(s => s.Debug.ShowDebugOverlay = _showDebugOverlay);
            if (_input.IsKeyPressed(InputKeys.KEY_F2))
                _settings.Update(s => s.Debug.ShowChunkBounds = _showDebugChunkBounds);
            if (_input.IsKeyPressed(InputKeys.KEY_F3))
                _settings.Update(s => s.Debug.ShowColliderRadii = _showColliderRadii);

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
                        if (_terrain is TerrainManager cancelTm)
                            cancelTm.SetWarmupMode(false);
                        _state = GameState.MainMenu;
                        _loadingCompleteTime = 0;
                        _loadingProgress = 0;
                        _loadingStageText = "Preparing world";
                        _loadingLoadedChunks = 0;
                        _loadingDesiredChunks = 0;
                        _loadingGeneratingChunks = 0;
                        _loadingEntities = 0;
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
            _playerEntity.SetComponent(cam);

            // Begin loading/warmup
            _state = GameState.Loading;
            if (_terrain is TerrainManager tm)
                tm.SetWarmupMode(true);
            _log.Debug("State transition requested: {State}", _state);
            _loadingProgress = 0;
            _loadingStageText = "Preparing world";
            _loadingLoadedChunks = 0;
            _loadingDesiredChunks = 0;
            _loadingGeneratingChunks = 0;
            _loadingEntities = 0;
            _loadingCompleteTime = 0;
        }

        private void LogBindings()
        {
            var k = _settings.Current.Keyboard;
            _log.Debug(
                "Effective movement bindings: Fwd=[{F1},{F2}] Back=[{B1},{B2}] Left=[{L1},{L2}] Right=[{R1},{R2}]",
                k.Forward.Primary, k.Forward.Secondary,
                k.Backward.Primary, k.Backward.Secondary,
                k.Left.Primary, k.Left.Secondary,
                k.Right.Primary, k.Right.Secondary);
        }

        private void LogStateTransition()
        {
            if (_state == _lastLoggedState) return;
            _lastLoggedState = _state;
            _log.Debug("Game state: {State}", _state);
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

            string entityText = $"Entities/POIs: {_loadingEntities}";
            int etw = _ui.MeasureText(entityText, 18);
            _ui.DrawText(entityText, w / 2 - etw / 2, y + barH + 58, 18, new Vector4(0.82f, 0.88f, 0.95f, 1f));

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
                    _ui.DrawText("Input", contentX, contentY, 20, new Vector4(0.75f, 0.9f, 1f, 1f));
                    contentY += 30;
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
                    _ui.DrawText("Display", contentX, contentY, 20, new Vector4(0.75f, 0.9f, 1f, 1f));
                    contentY += 30;

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
                    contentY += lineH;

                    DrawLabeledOption("Biome Crossfade", settings.Graphics.BiomeTextureCrossfade ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Graphics.BiomeTextureCrossfade = !s.Graphics.BiomeTextureCrossfade);
                        ApplySettings();
                    }
                    break;
                }
                case SettingsTab.Debug:
                {
                    _ui.DrawText("Debug", contentX, contentY, 28, Vector4.One);
                    contentY += 46;
                    int maxContentY = panelY + panelH - 120;
                    int compactLineH = 28;
                    bool compact = contentY + 14 * compactLineH > maxContentY;
                    if (compact)
                    {
                        lineH = compactLineH;
                    }
                    _ui.DrawText("Overlays", contentX, contentY, 20, new Vector4(0.75f, 0.9f, 1f, 1f));
                    contentY += compact ? 16 : 30;

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

                    DrawLabeledOption("Collider Radii (F3)", settings.Debug.ShowColliderRadii ? "On" : "Off", contentX, contentY);
                    if (Button("Toggle", new Rect(contentX + 360, contentY - 4, 92, 32), 18))
                    {
                        _settings.Update(s => s.Debug.ShowColliderRadii = !s.Debug.ShowColliderRadii);
                        ApplySettings();
                    }
                    contentY += lineH;

                    _ui.DrawText("Rendering & Rings", contentX, contentY, 20, new Vector4(0.75f, 0.9f, 1f, 1f));
                    contentY += compact ? 16 : 30;
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

                    _ui.DrawText("Gameplay", contentX, contentY, 20, new Vector4(0.75f, 0.9f, 1f, 1f));
                    contentY += compact ? 16 : 30;
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

            _showDebugOverlay = settings.Debug.ShowDebugOverlay;
            _showDebugChunkBounds = settings.Debug.ShowChunkBounds;
            _showColliderRadii = settings.Debug.ShowColliderRadii;

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
            bool click = hover && _uiLeftReleaseThisFrame && !_uiLeftReleaseConsumed;
            if (click)
                _uiLeftReleaseConsumed = true;

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
            var cam = _playerEntity.GetComponent<CameraComponent>();
            _debugOverlayUi.Draw(_ui, cam);
        }

        private void DrawChunkBoundsOverlay()
        {
            if (_terrain is not IDebugTerrain debugTerrain) return;
            if (!_playerEntity.TryGetComponent<CameraComponent>(out var camera)) return;

            var cam = _playerEntity.GetComponent<CameraComponent>();
            var info = debugTerrain.GetDebugInfo(cam.Position);
            float chunkWorld = info.ChunkSize * info.TileSize;
            float chunkMinX = info.ChunkX * chunkWorld;
            float chunkMinZ = info.ChunkZ * chunkWorld;
            float chunkMaxX = chunkMinX + chunkWorld;
            float chunkMaxZ = chunkMinZ + chunkWorld;

            // Sample terrain within the current chunk so the debug box hugs ground elevation.
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            const int samplesPerAxis = 5;
            for (int z = 0; z < samplesPerAxis; z++)
            for (int x = 0; x < samplesPerAxis; x++)
            {
                float tx = x / (float)(samplesPerAxis - 1);
                float tz = z / (float)(samplesPerAxis - 1);
                float wx = chunkMinX + tx * chunkWorld;
                float wz = chunkMinZ + tz * chunkWorld;
                float wy = _terrain.SampleHeight(new Vector3(wx, 0f, wz));
                if (wy < minY) minY = wy;
                if (wy > maxY) maxY = wy;
            }

            if (!float.IsFinite(minY) || !float.IsFinite(maxY))
                return;

            float midX = (chunkMinX + chunkMaxX) * 0.5f;
            float midZ = (chunkMinZ + chunkMaxZ) * 0.5f;
            const float groundOffset = 0.12f;
            int segments = Math.Max(4, info.ChunkSize / 2);

            // Ground guides: perimeter + cross lines through the center.
            var groundColor = new Vector4(0.2f, 0.95f, 0.2f, 1f);
            DrawTerrainPolyline(new Vector3(chunkMinX, 0f, chunkMinZ), new Vector3(chunkMaxX, 0f, chunkMinZ), segments, groundOffset, groundColor, camera);
            DrawTerrainPolyline(new Vector3(chunkMaxX, 0f, chunkMinZ), new Vector3(chunkMaxX, 0f, chunkMaxZ), segments, groundOffset, groundColor, camera);
            DrawTerrainPolyline(new Vector3(chunkMaxX, 0f, chunkMaxZ), new Vector3(chunkMinX, 0f, chunkMaxZ), segments, groundOffset, groundColor, camera);
            DrawTerrainPolyline(new Vector3(chunkMinX, 0f, chunkMaxZ), new Vector3(chunkMinX, 0f, chunkMinZ), segments, groundOffset, groundColor, camera);
            DrawTerrainPolyline(new Vector3(chunkMinX, 0f, midZ), new Vector3(chunkMaxX, 0f, midZ), segments, groundOffset, new Vector4(0.7f, 0.95f, 0.2f, 1f), camera);
            DrawTerrainPolyline(new Vector3(midX, 0f, chunkMinZ), new Vector3(midX, 0f, chunkMaxZ), segments, groundOffset, new Vector4(0.7f, 0.95f, 0.2f, 1f), camera);

            // Sky pillars at corners and center.
            float skyTop = MathF.Max(maxY + 35f, camera.Position.Y + 25f);
            var skyColor = new Vector4(0.35f, 0.95f, 1.0f, 0.95f);
            DrawSkyPillar(chunkMinX, chunkMinZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(chunkMaxX, chunkMinZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(chunkMaxX, chunkMaxZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(chunkMinX, chunkMaxZ, groundOffset, skyTop, skyColor, camera);
            DrawSkyPillar(midX, midZ, groundOffset, skyTop, new Vector4(0.95f, 0.95f, 0.2f, 0.95f), camera);
        }

        private void DrawTerrainPolyline(Vector3 start, Vector3 end, int segments, float heightOffset, Vector4 color, CameraComponent camera)
        {
            int segs = Math.Max(1, segments);
            Vector3 prev = start;
            prev.Y = _terrain.SampleHeight(prev) + heightOffset;

            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs;
                Vector3 cur = Vector3.Lerp(start, end, t);
                cur.Y = _terrain.SampleHeight(cur) + heightOffset;
                DrawProjectedLine(prev, cur, color, camera);
                prev = cur;
            }
        }

        private void DrawSkyPillar(float worldX, float worldZ, float groundOffset, float skyTop, Vector4 color, CameraComponent camera)
        {
            float yBase = _terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + groundOffset;
            DrawProjectedLine(new Vector3(worldX, yBase, worldZ), new Vector3(worldX, skyTop, worldZ), color, camera);
        }

        private void DrawProjectedWireframeBox(Vector3 min, Vector3 max, Vector4 color, CameraComponent camera)
        {
            Vector3[] corners =
            [
                new(min.X, min.Y, min.Z), // 0
                new(max.X, min.Y, min.Z), // 1
                new(max.X, min.Y, max.Z), // 2
                new(min.X, min.Y, max.Z), // 3
                new(min.X, max.Y, min.Z), // 4
                new(max.X, max.Y, min.Z), // 5
                new(max.X, max.Y, max.Z), // 6
                new(min.X, max.Y, max.Z)  // 7
            ];

            Span<Vector2> p = stackalloc Vector2[8];
            Span<bool> projected = stackalloc bool[8];
            for (int i = 0; i < corners.Length; i++)
            {
                projected[i] = TryProjectWorldToScreen(corners[i], camera, out p[i]);
            }

            static void DrawEdge(IUiProvider ui, Span<Vector2> pts, int a, int b, Vector4 c)
            {
                var pa = pts[a];
                var pb = pts[b];
                ui.DrawLine((int)pa.X, (int)pa.Y, (int)pb.X, (int)pb.Y, c);
            }

            if (projected[0] && projected[1]) DrawEdge(_ui, p, 0, 1, color);
            if (projected[1] && projected[2]) DrawEdge(_ui, p, 1, 2, color);
            if (projected[2] && projected[3]) DrawEdge(_ui, p, 2, 3, color);
            if (projected[3] && projected[0]) DrawEdge(_ui, p, 3, 0, color);
            if (projected[4] && projected[5]) DrawEdge(_ui, p, 4, 5, color);
            if (projected[5] && projected[6]) DrawEdge(_ui, p, 5, 6, color);
            if (projected[6] && projected[7]) DrawEdge(_ui, p, 6, 7, color);
            if (projected[7] && projected[4]) DrawEdge(_ui, p, 7, 4, color);
            if (projected[0] && projected[4]) DrawEdge(_ui, p, 0, 4, color);
            if (projected[1] && projected[5]) DrawEdge(_ui, p, 1, 5, color);
            if (projected[2] && projected[6]) DrawEdge(_ui, p, 2, 6, color);
            if (projected[3] && projected[7]) DrawEdge(_ui, p, 3, 7, color);
        }

        private void DrawColliderRadiiOverlay()
        {
            if (!_playerEntity.TryGetComponent<CameraComponent>(out var camera)) return;

            foreach (var entity in _entities.GetEntitiesWith<WorldObjectComponent, TransformComponent>())
            {
                if (!entity.TryGetComponent<ColliderComponent>(out var collider) || collider.Radius <= 0.01f)
                    continue;

                var t = entity.GetComponent<TransformComponent>();
                var toCamera = t.Position - camera.Position;
                if (toCamera.LengthSquared() > 120f * 120f)
                    continue;
                if (!TryProjectWorldToScreen(t.Position, camera, out var center2d)) continue;
                if (!TryProjectWorldToScreen(t.Position + Vector3.UnitX * collider.Radius, camera, out var edge2d)) continue;

                float radiusPx = Vector2.Distance(center2d, edge2d);
                if (radiusPx < 2f || radiusPx > 600f) continue;

                var color = new Vector4(1f, 0.42f, 0.2f, 0.9f);
                if (entity.TryGetComponent<CollisionFilterComponent>(out var filter) && filter.Layer == CollisionLayer.Foliage)
                    color = new Vector4(0.2f, 0.9f, 0.3f, 0.9f);
                DrawCircle(center2d, radiusPx, color, 20);
            }
        }

        private void DrawProjectedBoundsRectangle(Vector3 center, Vector3 size, Vector4 color, CameraComponent camera)
        {
            float hx = size.X * 0.5f;
            float hz = size.Z * 0.5f;
            Vector3[] corners =
            [
                new(center.X - hx, center.Y, center.Z - hz),
                new(center.X + hx, center.Y, center.Z - hz),
                new(center.X + hx, center.Y, center.Z + hz),
                new(center.X - hx, center.Y, center.Z + hz)
            ];

            Span<Vector2> projected = stackalloc Vector2[4];
            for (int i = 0; i < corners.Length; i++)
            {
                if (!TryProjectWorldToScreen(corners[i], camera, out projected[i]))
                    return;
            }

            for (int i = 0; i < 4; i++)
            {
                var a = projected[i];
                var b = projected[(i + 1) % 4];
                _ui.DrawLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, color);
            }
        }

        private void DrawCircle(Vector2 center, float radius, Vector4 color, int segments)
        {
            if (segments < 6) segments = 6;
            float step = (2f * MathF.PI) / segments;
            Vector2 prev = center + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float angle = i * step;
                Vector2 next = center + new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                _ui.DrawLine((int)prev.X, (int)prev.Y, (int)next.X, (int)next.Y, color);
                prev = next;
            }
        }

        private void DrawChunkAxesGizmo(Vector3 center, float axisLen, CameraComponent camera)
        {
            var origin = new Vector3(center.X, center.Y + 1f, center.Z);
            DrawProjectedLine(origin, origin + Vector3.UnitX * axisLen, new Vector4(1f, 0.25f, 0.25f, 1f), camera); // X
            DrawProjectedLine(origin, origin + Vector3.UnitY * axisLen, new Vector4(0.25f, 1f, 0.25f, 1f), camera); // Y
            DrawProjectedLine(origin, origin + Vector3.UnitZ * axisLen, new Vector4(0.25f, 0.6f, 1f, 1f), camera); // Z
        }

        private void DrawProjectedLine(Vector3 aWorld, Vector3 bWorld, Vector4 color, CameraComponent camera)
        {
            if (!TryProjectWorldToScreen(aWorld, camera, out var a)) return;
            if (!TryProjectWorldToScreen(bWorld, camera, out var b)) return;
            _ui.DrawLine((int)a.X, (int)a.Y, (int)b.X, (int)b.Y, color);
        }

        private bool TryProjectWorldToScreen(Vector3 world, CameraComponent camera, out Vector2 screen)
        {
            var forward = Vector3.Normalize(camera.Target - camera.Position);
            var right = Vector3.Normalize(Vector3.Cross(forward, camera.Up));
            var up = Vector3.Normalize(Vector3.Cross(right, forward));

            var rel = world - camera.Position;
            float xView = Vector3.Dot(rel, right);
            float yView = Vector3.Dot(rel, up);
            float zView = Vector3.Dot(rel, forward);
            if (zView <= 0.05f)
            {
                screen = Vector2.Zero;
                return false;
            }

            float aspect = Math.Max(1f, _graphics.ScreenWidth) / Math.Max(1f, _graphics.ScreenHeight);
            float fovRad = camera.FovY * (MathF.PI / 180f);
            float tanHalf = MathF.Tan(fovRad * 0.5f);
            if (tanHalf <= 1e-5f)
            {
                screen = Vector2.Zero;
                return false;
            }

            float xNdc = xView / (zView * tanHalf * aspect);
            float yNdc = yView / (zView * tanHalf);
            if (xNdc < -1.5f || xNdc > 1.5f || yNdc < -1.5f || yNdc > 1.5f)
            {
                screen = Vector2.Zero;
                return false;
            }

            float sx = (xNdc * 0.5f + 0.5f) * _graphics.ScreenWidth;
            float sy = (1f - (yNdc * 0.5f + 0.5f)) * _graphics.ScreenHeight;
            screen = new Vector2(sx, sy);
            return true;
        }

        private void DrawHotbar()
        {
            _hudUi.DrawHotbar(_ui, _graphics.ScreenWidth, _graphics.ScreenHeight, GetSelectedHotbarSlot());
        }

        private int GetSelectedHotbarSlot()
        {
            if (_playerEntity.TryGetComponent<HotbarSelectionComponent>(out var hotbar))
                return Math.Clamp(hotbar.SelectedSlot, 0, 8);
            return 0;
        }

        private void DrawCrosshair()
        {
            if (_crosshairEntity.TryGetComponent<UIElementComponent>(out var uiElement))
            {
                uiElement.Bounds = new Rect(_graphics.ScreenWidth / 2f, _graphics.ScreenHeight / 2f, 6f, 6f);
                _crosshairEntity.SetComponent(uiElement);
            }

            bool isHit = _crosshairEntity.TryGetComponent<UIElementComponent>(out var state) && state.Text == "hit";
            _hudUi.DrawCrosshair(_ui, _graphics.ScreenWidth, _graphics.ScreenHeight, isHit);
        }

    }
}
