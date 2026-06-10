using System.Numerics;
using Serilog;
using Veilborne.Ecs;
using Veilborne.Ecs.Components;
using Veilborne.GameFlow;
using Veilborne.Interfaces;
using Veilborne.Settings;
using Veilborne.Sky;
using Veilborne.UI;

namespace Veilborne
{
    public class VeilborneEngine : IGameEngine
    {
        private readonly IInfiniteTerrain _terrain;
        private readonly ITerrainStreaming _terrainStreaming;
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
        private readonly GameFlowController _flow = new();
        private readonly ILogger _log = Log.ForContext<VeilborneEngine>();

        private IUiProvider _ui;
        private bool _showDebugOverlay;
        private bool _isFullscreenApplied;
        private Entity _playerEntity = default!;
        private Entity _crosshairEntity = default!;
        private float _lastGameDt;
        private float _perfLogTimer;
        private GameFlowState _lastLoggedState = (GameFlowState)(-1);
        private volatile bool _splashReady;

        public VeilborneEngine(
            IInfiniteTerrain terrain,
            ITerrainStreaming terrainStreaming,
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
            _terrainStreaming = terrainStreaming;
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
            _menuRenderer = new GameMenuRenderer(settings, input, RuntimeEnvironment.IsDevelopmentEnvironment);
            _debugVizRenderer = new DebugVisualizationRenderer(terrain, entities);
        }

        public Task RunAsync()
        {
            _graphics.InitializeWindow(1280, 720, "Veilborne");
            ApplySettings(initialApply: true);

            _playerEntity = PlayerEntityFactory.CreateDefault(_entities, new Vector3(0, 5, -10));
            var hudUi = UiEntityFactory.CreateHudUi(_entities, _playerEntity.Id);
            _crosshairEntity = hudUi.Crosshair;

            _flow.SetInitialState(GameFlowState.MainMenu);
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
            if (_flow.ExitRequested)
            {
                _graphics.CloseWindow();
                return;
            }

            _input.UpdateStates();
            _time.Update(dt);
            _sky.Update(dt);
            _graphics.SetSkyClearColor(_sky.SkyColor);
            float gameDt = _time.DeltaTime;
            _lastGameDt = gameDt;

            HandleInput(gameDt);
            LogStateTransition();

            _flow.TickInitialization();

            if (_flow.ShouldUpdateEcs)
            {
                _ecsRuntime.UpdateSystems(gameDt);
                LogPerformanceSummary(gameDt);
            }
            else if (_flow.State == GameFlowState.Loading)
            {
                var cam = _playerEntity.GetComponent<CameraComponent>();
                if (_flow.UpdateLoading(gameDt, _terrainStreaming, cam.Position))
                {
                    _input.HideCursor();
                    _log.Debug("Entered Playing: cursor hidden and mouse lock requested.");
                }
                _playerEntity.SetComponent(cam);
            }
        }

        private void Draw3DStep()
        {
            if (!_flow.ShouldRender3D)
                return;

            var camera = _playerEntity.GetComponent<CameraComponent>();
            _graphics.Begin3D(camera);
            _ecsRuntime.RenderSystems(_lastGameDt, camera);
            _graphics.End3D();
        }

        private void Draw2DStep()
        {
            _time.NotifyFrameRendered();
            switch (_flow.State)
            {
                case GameFlowState.Initialization:
                    _menuRenderer.DrawInitializationScreen();
                    break;
                case GameFlowState.MainMenu:
                    ProcessMenuAction(_menuRenderer.DrawMainMenu(_splashReady));
                    break;
                case GameFlowState.Settings:
                    ProcessMenuAction(_menuRenderer.DrawSettingsMenu(() => ApplySettings()));
                    break;
                case GameFlowState.Loading:
                    _menuRenderer.DrawLoadingScreen(_flow.Loading.ToScreenData());
                    break;
                case GameFlowState.Playing:
                    DrawPlayingOverlay();
                    break;
                case GameFlowState.Paused:
                    _menuRenderer.DrawPauseOverlay();
                    ProcessMenuAction(_menuRenderer.DrawPauseMenu());
                    break;
            }
        }

        private void DrawPlayingOverlay()
        {
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
        }

        private void HandleInput(float dt)
        {
            _menuRenderer.BeginFrame(_input.IsMouseButtonReleased(InputKeys.MOUSE_BUTTON_LEFT));

            if (_flow.State == GameFlowState.Settings && _menuRenderer.IsCapturingBinding)
            {
                _menuRenderer.HandleBindingCaptureInput();
                return;
            }

            var keyboard = _settings.Current.Keyboard;

            if (KeyBindingTokens.IsPressed(_input, keyboard.DebugOverlay))
            {
                _showDebugOverlay = !_showDebugOverlay;
                _settings.Update(s => s.Debug.ShowDebugOverlay = _showDebugOverlay);
            }
            if (_input.IsKeyPressed(InputKeys.KEY_F10))
                _graphics.RequestScreenshot();
            if (KeyBindingTokens.IsPressed(_input, keyboard.Fullscreen))
            {
                _settings.Update(s => s.Graphics.Fullscreen = !s.Graphics.Fullscreen);
                ApplySettings();
            }

            switch (_flow.State)
            {
                case GameFlowState.MainMenu:
                    if (_input.IsKeyPressed(InputKeys.KEY_ENTER))
                        StartGame();
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                        _flow.RequestExit();
                    break;

                case GameFlowState.Loading:
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        var previous = _flow.State;
                        _flow.CancelLoading(_terrainStreaming);
                        SyncCursorAfterTransition(previous);
                    }
                    break;

                case GameFlowState.Playing:
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        var previous = _flow.State;
                        _flow.HandleEscapeKey(_terrainStreaming);
                        SyncCursorAfterTransition(previous);
                    }
                    break;

                case GameFlowState.Paused:
                    if (_input.IsKeyPressed(InputKeys.KEY_ESCAPE))
                    {
                        var previous = _flow.State;
                        _flow.HandleEscapeKey(_terrainStreaming);
                        SyncCursorAfterTransition(previous);
                    }
                    break;

                case GameFlowState.Settings:
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

            _flow.BeginLoading(_terrainStreaming);
            _log.Debug("State transition requested: {State}", _flow.State);
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
            if (_flow.State == _lastLoggedState) return;
            _lastLoggedState = _flow.State;
            _log.Debug("Game state: {State}", _flow.State);
        }

        private void ProcessMenuAction(MenuAction action)
        {
            if (action == MenuAction.None)
                return;

            var previous = _flow.State;
            if (action == MenuAction.StartGame)
                StartGame();
            else
            {
                if (action == MenuAction.OpenSettings)
                    _menuRenderer.ResetForSettings();
                _flow.ApplyMenuAction(action, _terrainStreaming);
            }

            SyncCursorAfterTransition(previous);
        }

        private void ReturnFromSettings()
        {
            var previous = _flow.State;
            _flow.ReturnFromSettings();
            SyncCursorAfterTransition(previous);
        }

        private void SyncCursorAfterTransition(GameFlowState previousState)
        {
            if (_flow.State == previousState)
                return;

            if (_flow.ShouldHideCursor())
                _input.HideCursor();
            else if (_flow.ShouldShowCursor())
                _input.ShowCursor();
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

        private void DrawPerformanceOverlay() => _debugOverlayUi.DrawPerformanceOverlay(_ui);

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
