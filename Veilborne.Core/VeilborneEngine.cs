using System.Numerics;
using Serilog;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.Interfaces;
using Veilborne.Settings;
using Veilborne.Sky;
using Veilborne.Terrain;
using Veilborne.UI;

namespace Veilborne
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
        private readonly GameMenuRenderer _menuRenderer;
        private readonly DebugVisualizationRenderer _debugVizRenderer;
        private readonly EcsPerformanceMonitor? _perfMonitor;
        private readonly bool _isDevelopmentEnvironment;
        private readonly ILogger _log = Log.ForContext<VeilborneEngine>();

        // These will be initialized by ECS manager after MonoGame is ready
        private IUiProvider _ui;

        private bool _showDebugOverlay;
        private bool _isFullscreenApplied;
        private Entity _playerEntity = default!;
        private Entity _uiCanvasEntity = default!;
        private Entity _crosshairEntity = default!;
        private float _lastGameDt;
        private float _perfLogTimer;

        // Simple UI state machine
        private enum GameState { Initialization, MainMenu, Settings, Loading, Playing, Paused }
        private enum SettingsReturnState { MainMenu, Paused }
        private GameState _state = GameState.MainMenu;
        private SettingsReturnState _settingsReturnState = SettingsReturnState.MainMenu;

        // Loading state
        private float _loadingProgress;
        private string _loadingStageText = "Preparing world";
        private int _loadingLoadedChunks;
        private int _loadingDesiredChunks;
        private int _loadingGeneratingChunks;
        private int _loadingEntities;
        private int _loadingPendingSpawnObjects;
        private double _loadingCompleteTime;
        private Task? _loadingPumpTask;
        private bool _requestedExit;
        private GameState _lastLoggedState = (GameState)(-1);
        private volatile bool _splashReady;

        private const double LoadingCompletionDelay = 0.10;

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
            DebugOverlayUiController debugOverlayUi,
            EcsPerformanceMonitor? perfMonitor = null)
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
            _perfMonitor = perfMonitor;
            _isDevelopmentEnvironment = RuntimeEnvironment.IsDevelopmentEnvironment;
            _menuRenderer = new GameMenuRenderer(settings, input, _isDevelopmentEnvironment);
            _debugVizRenderer = new DebugVisualizationRenderer(terrain, entities);
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
            _uiCanvasEntity.AddComponent(new ChildrenComponent { EntityIds = [] });

            _crosshairEntity = _entities.CreateEntity();
            _crosshairEntity.AddComponent(new ParentComponent { EntityId = _uiCanvasEntity.Id });
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
                LogPerformanceSummary(gameDt);
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
                    if (_loadingPumpTask is { IsCompleted: true, IsFaulted: true })
                        _ = _loadingPumpTask.Exception;
                    if (_loadingPumpTask is null || _loadingPumpTask.IsCompleted)
                        _loadingPumpTask = tm.PumpAsyncJobs();
                    var loading = tm.GetLoadingProgress();
                    // Keep loading bar monotonic to avoid visible back-and-forth flicker
                    // when desired chunk counts/radii adjust during warmup.
                    _loadingProgress = MathF.Max(_loadingProgress, loading.Progress01);
                    _loadingStageText = loading.Stage;
                    _loadingDesiredChunks = loading.DesiredChunks;
                    _loadingLoadedChunks = loading.LoadedChunks;
                    _loadingGeneratingChunks = loading.GeneratingChunks;
                    _loadingEntities = loading.LoadedEntities;
                    _loadingPendingSpawnObjects = loading.PendingSpawnObjects;
                }
                else
                {
                    _loadingPumpTask = null;
                    _loadingProgress = 1f;
                    _loadingStageText = "Complete";
                    _loadingDesiredChunks = 0;
                    _loadingLoadedChunks = 0;
                    _loadingGeneratingChunks = 0;
                    _loadingEntities = 0;
                    _loadingPendingSpawnObjects = 0;
                }

                bool loadingReady = _loadingProgress >= 0.999f &&
                                    _loadingGeneratingChunks == 0 &&
                                    _loadingLoadedChunks >= _loadingDesiredChunks &&
                                    _loadingPendingSpawnObjects == 0;
                if (loadingReady)
                    _loadingCompleteTime += dt;
                else
                    _loadingCompleteTime = 0;

                if (_loadingCompleteTime >= LoadingCompletionDelay)
                {
                    if (_terrain is TerrainManager readyTm)
                        readyTm.SetWarmupMode(false);
                    _state = GameState.Playing;
                    _loadingPumpTask = null;
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
                case GameState.Initialization:
                    _menuRenderer.DrawInitializationScreen();
                    break;
                case GameState.MainMenu:
                    ProcessMenuAction(_menuRenderer.DrawMainMenu(_splashReady));
                    break;
                case GameState.Settings:
                    ProcessMenuAction(_menuRenderer.DrawSettingsMenu(() => ApplySettings()));
                    break;
                case GameState.Loading:
                    _menuRenderer.DrawLoadingScreen(new LoadingScreenData(
                        _loadingProgress, _loadingStageText,
                        _loadingLoadedChunks, _loadingDesiredChunks,
                        _loadingGeneratingChunks, _loadingEntities));
                    break;
                case GameState.Playing:
                    if (_showDebugOverlay) DrawDebugOverlay();
                    if (_settings.Current.Debug.ShowPerformanceOverlay) DrawPerformanceOverlay();
                    if (_settings.Current.Debug.ShowChunkBounds)
                    {
                        var cam = _playerEntity.GetComponent<CameraComponent>();
                        _debugVizRenderer.DrawChunkBoundsOverlay(cam);
                    }
                    if (_settings.Current.Debug.ShowColliderRadii)
                    {
                        var cam = _playerEntity.GetComponent<CameraComponent>();
                        _debugVizRenderer.DrawColliderRadiiOverlay(cam);
                    }
                    if (_settings.Current.General.ShowCrosshair) DrawCrosshair();
                    DrawHotbar();
                    break;
                case GameState.Paused:
                    _menuRenderer.DrawPauseOverlay();
                    ProcessMenuAction(_menuRenderer.DrawPauseMenu());
                    break;
            }
        }

        private void HandleInput(float dt)
        {
            _menuRenderer.BeginFrame(_input.IsMouseButtonReleased(InputKeys.MOUSE_BUTTON_LEFT));

            if (_state == GameState.Settings && _menuRenderer.IsCapturingBinding)
            {
                _menuRenderer.HandleBindingCaptureInput();
                return;
            }

            var keyboard = _settings.Current.Keyboard;

            // Global toggles (available in all states)
            if (KeyBindingTokens.IsPressed(_input, keyboard.DebugOverlay))
            {
                _showDebugOverlay = !_showDebugOverlay;
                _settings.Update(s => s.Debug.ShowDebugOverlay = _showDebugOverlay);
            }
            if (_input.IsKeyPressed(InputKeys.KEY_F10)) _graphics.RequestScreenshot();

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
                        _loadingPumpTask = null;
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
                    _menuRenderer.HandleSettingsInput();
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                        ReturnFromSettings();
                    break;
            }
        }

        private void LoadUiAssets()
        {
            _graphics.SetWindowIcon("assets\\logo.svg");
            _menuRenderer.Initialize(_ui, _graphics);
            _debugVizRenderer.Initialize(_ui, _graphics);
            // Load splash texture asynchronously to avoid blocking the first frame
            _splashReady = false;
            _ = Task.Run(() =>
            {
                try
                {
                    _ui.RegisterSvgTexture(GameMenuRenderer.SplashTextureKey, "assets\\splash.svg", 2000, 1200);
                }
                catch (Exception ex)
                {
                    _log.Warning(ex, "Failed to load splash texture asynchronously");
                }
                finally
                {
                    _splashReady = true;
                }
            });
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
            _loadingPumpTask = null;
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

        private void ProcessMenuAction(MenuAction action)
        {
            switch (action)
            {
                case MenuAction.StartGame:
                    StartGame();
                    break;
                case MenuAction.OpenSettings:
                    OpenSettings(_state == GameState.Paused
                        ? SettingsReturnState.Paused
                        : SettingsReturnState.MainMenu);
                    break;
                case MenuAction.ExitApplication:
                    _requestedExit = true;
                    break;
                case MenuAction.Resume:
                    _state = GameState.Playing;
                    _input.HideCursor();
                    break;
                case MenuAction.ExitToMenu:
                    _state = GameState.MainMenu;
                    _input.ShowCursor();
                    break;
                case MenuAction.Back:
                    ReturnFromSettings();
                    break;
            }
        }

        private void OpenSettings(SettingsReturnState returnState)
        {
            _settingsReturnState = returnState;
            _state = GameState.Settings;
            _menuRenderer.ResetForSettings();
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

        private void DrawDebugOverlay()
        {
            var cam = _playerEntity.GetComponent<CameraComponent>();
            _debugOverlayUi.Draw(_ui, cam);
        }

        private void DrawPerformanceOverlay()
        {
            _debugOverlayUi.DrawPerformanceOverlay(_ui);
        }

        private const float PerfLogIntervalSeconds = 5f;

        private void LogPerformanceSummary(float dt)
        {
            if (_perfMonitor == null || !_settings.Current.Debug.EnablePerformanceLogging) return;
            _perfLogTimer += dt;
            if (_perfLogTimer < PerfLogIntervalSeconds) return;
            _perfLogTimer = 0f;

            int fps = _time.Fps;
            var totals = _perfMonitor.GetLastFrameTotals();
            var allTimings = _perfMonitor.GetAllTimings();
            var customMetrics = _perfMonitor.GetCustomMetrics();

            _log.Information("=== PERF SUMMARY (FPS: {Fps}) ===", fps);
            _log.Information("  ECS Update: {UpdateMs:0.00}ms  Render: {RenderMs:0.00}ms  Total: {TotalMs:0.00}ms",
                totals.updateMs, totals.renderMs, totals.updateMs + totals.renderMs);

            if (customMetrics.Count > 0)
            {
                var parts = new System.Text.StringBuilder("  Terrain:");
                foreach (var kvp in customMetrics)
                    parts.Append($" {kvp.Key}={kvp.Value:0.#}");
                _log.Information(parts.ToString());
            }

            _log.Information("  ── All Systems (sorted by avg total) ──");
            for (int i = 0; i < allTimings.Count; i++)
            {
                var t = allTimings[i];
                _log.Information("  {Rank,2}. {Name,-35} U:{AvgU:0.00}ms  R:{AvgR:0.00}ms  Pk:{Peak:0.00}ms",
                    i + 1, t.Name, t.AvgUpdateMs, t.AvgRenderMs,
                    Math.Max(t.PeakUpdateMs, t.PeakRenderMs));
            }
            _log.Information("=== END PERF SUMMARY ===");
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
