using System.Numerics;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Settings;

namespace Veilborne.Core.UI
{
    public enum MenuAction
    {
        None,
        StartGame,
        OpenSettings,
        ExitApplication,
        Resume,
        ExitToMenu,
        Back
    }

    public readonly record struct LoadingScreenData(
        float Progress,
        string StageText,
        int LoadedChunks,
        int DesiredChunks,
        int GeneratingChunks,
        int LoadedEntities);

    /// <summary>
    /// Renders all game menus (main, pause, settings, loading, initialization).
    /// Owns settings-tab state and key-binding capture flow.
    /// Returns <see cref="MenuAction"/> so the caller can apply state transitions.
    /// </summary>
    public sealed class GameMenuRenderer
    {
        private readonly IGameSettingsService _settings;
        private readonly IInputProvider _input;
        private readonly bool _isDevelopmentEnvironment;

        private IUiProvider _ui = null!;
        private IGraphicsProvider _graphics = null!;

        private enum SettingsTab { General, Graphics, Keyboard, Debug }

        private enum KeyboardAction
        {
            Forward, Backward, Left, Right, Jump, DigInteract,
            DebugOverlay, Fullscreen,
            Hotbar1, Hotbar2, Hotbar3, Hotbar4, Hotbar5,
            Hotbar6, Hotbar7, Hotbar8, Hotbar9, Scroll
        }

        private SettingsTab _settingsTab = SettingsTab.General;
        private int _tabScrollOffset;
        private bool _isCapturingBinding;
        private KeyboardAction _capturingAction;
        private bool _capturingPrimary = true;
        private int _captureIgnoreFrames;
        private bool _uiLeftReleaseThisFrame;
        private bool _uiLeftReleaseConsumed;

        public const string SplashTextureKey = "ui/splash";

        public bool IsCapturingBinding => _isCapturingBinding;

        public GameMenuRenderer(IGameSettingsService settings, IInputProvider input, bool isDevelopmentEnvironment)
        {
            _settings = settings;
            _input = input;
            _isDevelopmentEnvironment = isDevelopmentEnvironment;
        }

        public void Initialize(IUiProvider ui, IGraphicsProvider graphics)
        {
            _ui = ui;
            _graphics = graphics;
        }

        /// <summary>Call once per update frame before any Draw calls.</summary>
        public void BeginFrame(bool leftReleased)
        {
            _uiLeftReleaseThisFrame = leftReleased;
            _uiLeftReleaseConsumed = false;
            if (_captureIgnoreFrames > 0)
                _captureIgnoreFrames--;
        }

        /// <summary>Reset internal tab/capture state when entering the settings screen.</summary>
        public void ResetForSettings()
        {
            _settingsTab = SettingsTab.General;
            _tabScrollOffset = 0;
            _isCapturingBinding = false;
        }

        /// <summary>Handle tab scroll and binding capture input in Settings state.</summary>
        public void HandleSettingsInput()
        {
            if (_isCapturingBinding) return;
            HandleTabScrollInput();
        }

        // ── Main Menu ──────────────────────────────────────────────

        public MenuAction DrawMainMenu(bool splashReady)
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _graphics.Clear(new Vector3(15 / 255f, 18 / 255f, 22 / 255f));

            int btnW = Math.Min(340, (int)(w * 0.35f));
            int btnH = 52;
            int xCenter = w / 2 - btnW / 2;
            int startY = (int)(h * 0.62f);
            int gap = btnH + 14;

            bool drewSplash = false;
            if (splashReady && _ui.HasTexture(SplashTextureKey) &&
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
                return MenuAction.StartGame;

            if (Button("Settings", new Rect(xCenter, startY + gap, btnW, btnH)))
                return MenuAction.OpenSettings;

            if (Button("Exit", new Rect(xCenter, startY + gap * 2, btnW, btnH)))
                return MenuAction.ExitApplication;

            return MenuAction.None;
        }

        // ── Loading Screen ─────────────────────────────────────────

        public void DrawLoadingScreen(in LoadingScreenData data)
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _graphics.Clear(new Vector3(10 / 255f, 12 / 255f, 16 / 255f));

            string title = "Loading world...";
            int tw = _ui.MeasureText(title, 30);
            _ui.DrawText(title, w / 2 - tw / 2, h / 2 - 80, 30, Vector4.One);

            int barW = Math.Min(500, (int)(w * 0.6f));
            int barH = 24;
            int x = w / 2 - barW / 2;
            int y = h / 2 - barH / 2;
            _ui.DrawRectangle(x, y, barW, barH, new Vector4(30 / 255f, 35 / 255f, 42 / 255f, 1.0f));
            int filled = (int)(barW * Math.Clamp(data.Progress, 0f, 1f));
            _ui.DrawRectangle(x, y, filled, barH, new Vector4(100 / 255f, 200 / 255f, 255 / 255f, 1.0f));
            _ui.DrawRectangleLines(x, y, barW, barH, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

            string progressText = $"{Math.Clamp((int)(data.Progress * 100f), 0, 100)}%";
            string stageText = string.IsNullOrWhiteSpace(data.StageText) ? "Preparing world" : data.StageText;
            int stw = _ui.MeasureText(stageText, 20);
            _ui.DrawText(stageText, w / 2 - stw / 2, y + barH + 10, 20, Vector4.One);

            string chunkText = data.DesiredChunks > 0
                ? $"Chunks: {data.LoadedChunks}/{data.DesiredChunks}" +
                  (data.GeneratingChunks > 0 ? $" (generating {data.GeneratingChunks})" : "")
                : "Chunks: preparing";
            int ctw = _ui.MeasureText(chunkText, 18);
            _ui.DrawText(chunkText, w / 2 - ctw / 2, y + barH + 36, 18, new Vector4(0.82f, 0.88f, 0.95f, 1f));

            string entityText = $"Entities/POIs: {data.LoadedEntities}";
            int etw = _ui.MeasureText(entityText, 18);
            _ui.DrawText(entityText, w / 2 - etw / 2, y + barH + 58, 18, new Vector4(0.82f, 0.88f, 0.95f, 1f));

            int ptw = _ui.MeasureText(progressText, 20);
            _ui.DrawText(progressText, w / 2 - ptw / 2, y - 30, 20, Vector4.One);
        }

        // ── Initialization Screen ──────────────────────────────────

        public void DrawInitializationScreen()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;
            _graphics.Clear(new Vector3(10 / 255f, 12 / 255f, 16 / 255f));

            int texW = 2000;
            int texH = 1200;
            int maxW = (int)(w * 0.85f);
            int maxH = (int)(h * 0.65f);
            float scale = MathF.Min(maxW / (float)Math.Max(texW, 1), maxH / (float)Math.Max(texH, 1));
            int drawW = (int)(Math.Max(1, texW) * scale);
            int drawH = (int)(Math.Max(1, texH) * scale);
            int x = w / 2 - drawW / 2;
            int y = h / 2 - drawH / 2 - 10;

            _ui.DrawTexture(SplashTextureKey, x, y, scale, Vector4.One);
            int contentBottom = y + drawH;

            float p = 1f;
            string stage = "Complete";
            string title = stage != "Complete" ? "Initializing Textures..." : "Initializing Veilborne...";
            int tw2 = _ui.MeasureText(title, 24);

            int barW = Math.Min(520, (int)(w * 0.6f));
            int barH = 24;
            int marginAboveBar = 40;
            int marginBelowArt = 28;

            int titleY = contentBottom + marginBelowArt;
            int maxTitleY = Math.Max(0, h - (barH + marginAboveBar + 20 + 30 + 24));
            if (titleY > maxTitleY) titleY = maxTitleY;
            if (titleY < (int)(h * 0.6f)) titleY = (int)(h * 0.6f);

            _ui.DrawText(title, w / 2 - tw2 / 2, titleY, 24, Vector4.One);

            int bx = w / 2 - barW / 2;
            int by = titleY + marginAboveBar;
            _ui.DrawRectangle(bx, by, barW, barH, new Vector4(30 / 255f, 35 / 255f, 42 / 255f, 1.0f));
            int filled = (int)(barW * Math.Clamp(p, 0f, 1f));
            _ui.DrawRectangle(bx, by, filled, barH, new Vector4(100 / 255f, 200 / 255f, 255 / 255f, 1.0f));
            _ui.DrawRectangleLines(bx, by, barW, barH, new Vector4(0.5f, 0.5f, 0.5f, 1.0f));

            string pct = $"{Math.Clamp((int)(p * 100), 0, 100)}%";
            string stageText = string.IsNullOrWhiteSpace(stage) ? pct : $"{stage}  {pct}";
            int stw = _ui.MeasureText(stageText, 20);
            _ui.DrawText(stageText, w / 2 - stw / 2, by + barH + 10, 20, Vector4.One);
        }

        // ── Pause ──────────────────────────────────────────────────

        public void DrawPauseOverlay()
        {
            _ui.DrawRectangle(0, 0, _graphics.ScreenWidth, _graphics.ScreenHeight,
                new Vector4(0, 0, 0, 160 / 255f));
        }

        public MenuAction DrawPauseMenu()
        {
            int w = _graphics.ScreenWidth;
            int h = _graphics.ScreenHeight;

            int btnW = Math.Min(380, (int)(w * 0.4f));
            int btnH = 56;
            int xCenter = w / 2 - btnW / 2;
            int startY = (int)(h * 0.4f);

            if (Button("Resume", new Rect(xCenter, startY, btnW, btnH)))
                return MenuAction.Resume;
            if (Button("Settings", new Rect(xCenter, startY + btnH + 14, btnW, btnH)))
                return MenuAction.OpenSettings;
            if (Button("Exit to Menu", new Rect(xCenter, startY + (btnH + 14) * 2, btnW, btnH)))
                return MenuAction.ExitToMenu;
            if (Button("Exit to Desktop", new Rect(xCenter, startY + (btnH + 14) * 3, btnW, btnH)))
                return MenuAction.ExitApplication;

            return MenuAction.None;
        }

        // ── Settings ───────────────────────────────────────────────

        public MenuAction DrawSettingsMenu(Action applySettings)
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

            _ui.DrawText("Settings", panelX + 24, panelY + 18, 34, Vector4.One);

            int tabY = panelY + 72;
            int tabW = 130;
            int tabH = 42;
            int tabGap = 10;
            var prevTab = _settingsTab;
            if (Button("General", new Rect(panelX + 24, tabY, tabW, tabH), 22)) _settingsTab = SettingsTab.General;
            if (Button("Graphics", new Rect(panelX + 24 + (tabW + tabGap), tabY, tabW, tabH), 22)) _settingsTab = SettingsTab.Graphics;
            if (Button("Keyboard", new Rect(panelX + 24 + (tabW + tabGap) * 2, tabY, tabW, tabH), 22)) _settingsTab = SettingsTab.Keyboard;
            if (_isDevelopmentEnvironment &&
                Button("Debug", new Rect(panelX + 24 + (tabW + tabGap) * 3, tabY, tabW, tabH), 22))
                _settingsTab = SettingsTab.Debug;
            if (!_isDevelopmentEnvironment && _settingsTab == SettingsTab.Debug)
                _settingsTab = SettingsTab.General;
            if (_settingsTab != prevTab) _tabScrollOffset = 0;

            int contentX = panelX + 30;
            int contentY = tabY + tabH + 18;
            int lineH = 40;

            var settings = _settings.Current;
            switch (_settingsTab)
            {
                case SettingsTab.General:
                    DrawGeneralTab(contentX, contentY, lineH, panelX, panelY, panelW, panelH, settings);
                    break;
                case SettingsTab.Graphics:
                    DrawGraphicsTab(contentX, contentY, lineH, panelX, panelY, panelW, panelH, settings, applySettings);
                    break;
                case SettingsTab.Debug:
                    DrawDebugTab(contentX, contentY, lineH, panelX, panelY, panelW, panelH, settings, applySettings);
                    break;
                case SettingsTab.Keyboard:
                    DrawKeyboardSettingsPanel(panelX, panelY, panelW, panelH, contentX, contentY, lineH);
                    break;
            }

            if (Button("Back", new Rect(panelX + panelW - 170, panelY + panelH - 62, 140, 40), 22))
            {
                if (_isCapturingBinding)
                {
                    _isCapturingBinding = false;
                    _captureIgnoreFrames = 2;
                    return MenuAction.None;
                }
                return MenuAction.Back;
            }

            return MenuAction.None;
        }

        // ── Settings tab helpers ───────────────────────────────────

        private void DrawGeneralTab(int contentX, int contentY, int lineH, int panelX, int panelY, int panelW, int panelH, GameSettings settings)
        {
            _ui.DrawText("General", contentX, contentY, 28, Vector4.One);
            int contentTop = contentY + 46;
            int usableBottom = panelY + panelH - 108;
            int visibleRows = Math.Max(1, (usableBottom - contentTop) / lineH);

            var rows = new (string label, string value, Action? onToggle, Action? onMinus, Action? onPlus)[]
            {
                ("Input", null!, null, null, null), // section header
                ("Mouse Sensitivity", $"{settings.General.MouseSensitivity:0.0000}", null,
                    () => _settings.Update(s => s.General.MouseSensitivity -= 0.0005f),
                    () => _settings.Update(s => s.General.MouseSensitivity += 0.0005f)),
                ("Invert Mouse Y", settings.General.InvertMouseY ? "On" : "Off",
                    () => _settings.Update(s => s.General.InvertMouseY = !s.General.InvertMouseY), null, null),
                ("Show Crosshair", settings.General.ShowCrosshair ? "On" : "Off",
                    () => _settings.Update(s => s.General.ShowCrosshair = !s.General.ShowCrosshair), null, null),
            };

            DrawScrollableRows(rows, contentX, contentTop, lineH, visibleRows, panelX, panelY, panelW, panelH, null);
        }

        private void DrawGraphicsTab(int contentX, int contentY, int lineH, int panelX, int panelY, int panelW, int panelH, GameSettings settings, Action applySettings)
        {
            _ui.DrawText("Graphics", contentX, contentY, 28, Vector4.One);
            int contentTop = contentY + 46;
            int usableBottom = panelY + panelH - 108;
            int visibleRows = Math.Max(1, (usableBottom - contentTop) / lineH);

            var rows = new (string label, string value, Action? onToggle, Action? onMinus, Action? onPlus)[]
            {
                ("Display", null!, null, null, null),
                ("Target FPS", $"{settings.Graphics.TargetFps}", null,
                    () => { _settings.Update(s => s.Graphics.TargetFps = Math.Max(30, s.Graphics.TargetFps - 10)); applySettings(); },
                    () => { _settings.Update(s => s.Graphics.TargetFps = Math.Min(240, s.Graphics.TargetFps + 10)); applySettings(); }),
                ("Fullscreen", settings.Graphics.Fullscreen ? "On" : "Off",
                    () => { _settings.Update(s => s.Graphics.Fullscreen = !s.Graphics.Fullscreen); applySettings(); }, null, null),
                ("Draw Distance", null!, null, null, null),
                ("Terrain View Distance", $"{settings.Graphics.TerrainViewDistance}%", null,
                    () => { _settings.Update(s => s.Graphics.TerrainViewDistance = Math.Max(20, s.Graphics.TerrainViewDistance - 5)); applySettings(); },
                    () => { _settings.Update(s => s.Graphics.TerrainViewDistance = Math.Min(200, s.Graphics.TerrainViewDistance + 5)); applySettings(); }),
                ("Object View Distance", $"{settings.Graphics.ObjectViewDistance}%", null,
                    () => { _settings.Update(s => s.Graphics.ObjectViewDistance = Math.Max(20, s.Graphics.ObjectViewDistance - 5)); applySettings(); },
                    () => { _settings.Update(s => s.Graphics.ObjectViewDistance = Math.Min(200, s.Graphics.ObjectViewDistance + 5)); applySettings(); }),
                ("Visual", null!, null, null, null),
                ("Brightness", $"{settings.Graphics.Brightness}%", null,
                    () => { _settings.Update(s => s.Graphics.Brightness = Math.Max(50, s.Graphics.Brightness - 5)); applySettings(); },
                    () => { _settings.Update(s => s.Graphics.Brightness = Math.Min(150, s.Graphics.Brightness + 5)); applySettings(); }),
                ("Biome Crossfade", settings.Graphics.BiomeTextureCrossfade ? "On" : "Off",
                    () => { _settings.Update(s => s.Graphics.BiomeTextureCrossfade = !s.Graphics.BiomeTextureCrossfade); applySettings(); }, null, null),
            };

            DrawScrollableRows(rows, contentX, contentTop, lineH, visibleRows, panelX, panelY, panelW, panelH, null);
        }

        private void DrawDebugTab(int contentX, int contentY, int lineH, int panelX, int panelY, int panelW, int panelH, GameSettings settings, Action applySettings)
        {
            _ui.DrawText("Debug", contentX, contentY, 28, Vector4.One);
            int contentTop = contentY + 46;
            int usableBottom = panelY + panelH - 108;
            int visibleRows = Math.Max(1, (usableBottom - contentTop) / lineH);

            var rows = new (string label, string value, Action? onToggle, Action? onMinus, Action? onPlus)[]
            {
                // Player overlay
                ("Player Overlays", null!, null, null, null),
                ("FPS & Info Overlay (F1)", settings.Debug.ShowDebugOverlay ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.ShowDebugOverlay = !s.Debug.ShowDebugOverlay); applySettings(); }, null, null),

                // Developer overlays
                ("Developer Overlays", null!, null, null, null),
                ("Chunk Outline Overlay", settings.Debug.ShowChunkBounds ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.ShowChunkBounds = !s.Debug.ShowChunkBounds); applySettings(); }, null, null),
                ("Collider Radii", settings.Debug.ShowColliderRadii ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.ShowColliderRadii = !s.Debug.ShowColliderRadii); applySettings(); }, null, null),
                ("ECS Performance Overlay", settings.Debug.ShowPerformanceOverlay ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.ShowPerformanceOverlay = !s.Debug.ShowPerformanceOverlay); applySettings(); }, null, null),
                ("Performance Logging", settings.Debug.EnablePerformanceLogging ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.EnablePerformanceLogging = !s.Debug.EnablePerformanceLogging); applySettings(); }, null, null),
                ("Wireframe", settings.Debug.Wireframe ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.Wireframe = !s.Debug.Wireframe); applySettings(); }, null, null),

                // LOD ring visibility
                ("LOD Ring Visibility", null!, null, null, null),
                ("Show Editable Ring", settings.Debug.ShowEditableRing ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.ShowEditableRing = !s.Debug.ShowEditableRing); applySettings(); }, null, null),
                ("Show ReadOnly Ring", settings.Debug.ShowReadOnlyRing ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.ShowReadOnlyRing = !s.Debug.ShowReadOnlyRing); applySettings(); }, null, null),
                ("Show LowLod Ring", settings.Debug.ShowLowLodRing ? "On" : "Off",
                    () => { _settings.Update(s => s.Debug.ShowLowLodRing = !s.Debug.ShowLowLodRing); applySettings(); }, null, null),

                // Gameplay
                ("Gameplay", null!, null, null, null),
                ("Run Speed Multiplier", $"{settings.Debug.RunSpeedMultiplier}%", null,
                    () => { _settings.Update(s => s.Debug.RunSpeedMultiplier = Math.Max(50, s.Debug.RunSpeedMultiplier - 10)); applySettings(); },
                    () => { _settings.Update(s => s.Debug.RunSpeedMultiplier = Math.Min(300, s.Debug.RunSpeedMultiplier + 10)); applySettings(); }),
            };

            DrawScrollableRows(rows, contentX, contentTop, lineH, visibleRows, panelX, panelY, panelW, panelH, null);
        }

        // ── Keyboard settings ──────────────────────────────────────

        private void DrawKeyboardSettingsPanel(int panelX, int panelY, int panelW, int panelH,
            int contentX, int contentY, int lineH)
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
            _tabScrollOffset = Math.Clamp(_tabScrollOffset, 0, maxOffset);
            int startIndex = _tabScrollOffset;
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
                int thumbY = barY + (thumbTravel == 0 ? 0 : (int)(thumbTravel * (_tabScrollOffset / (float)maxOffset)));
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

        private void HandleTabScrollInput()
        {
            int deltaRows = 0;
            float wheel = _input.GetMouseWheelMove();
            if (wheel > 0) deltaRows -= 1;
            if (wheel < 0) deltaRows += 1;
            if (_input.IsKeyPressed(InputKeys.KEY_UP)) deltaRows -= 1;
            if (_input.IsKeyPressed(InputKeys.KEY_DOWN)) deltaRows += 1;

            if (deltaRows != 0)
            {
                _tabScrollOffset += deltaRows;
                // Soft-clamp to a reasonable range if not already clamped by a Draw call.
                // We'll clamp to 0 here, and the upper bound will be handled in DrawScrollableRows.
                // To avoid "scrolling off into infinity", we also apply a large upper bound safety
                // until the next Draw call precisely clamps it to the row count.
                if (_tabScrollOffset < 0) _tabScrollOffset = 0;
                if (_tabScrollOffset > 500) _tabScrollOffset = 500;
            }
        }

        private static (string label, KeyboardAction action, bool disabled)[] GetKeyboardActionRows()
        {
            return
            [
                ("Forward", KeyboardAction.Forward, false),
                ("Backward", KeyboardAction.Backward, false),
                ("Left", KeyboardAction.Left, false),
                ("Right", KeyboardAction.Right, false),
                ("Jump", KeyboardAction.Jump, false),
                ("Dig / Interact", KeyboardAction.DigInteract, false),
                ("Debug Overlay", KeyboardAction.DebugOverlay, false),
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
            ];
        }

        private void DrawKeyboardBindingRow(string label, KeyboardAction action, int x, int y,
            int rowWidth, bool disabled = false)
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

        // ── Key-binding capture ────────────────────────────────────

        public void HandleBindingCaptureInput()
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

        // ── Binding helpers ────────────────────────────────────────

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

        private static string GetActionLabel(KeyboardAction action)
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

        // ── Shared UI primitives ───────────────────────────────────

        /// <summary>Draws a scrollable list of option rows with section headers, toggles, and +/- buttons.</summary>
        private void DrawScrollableRows(
            (string label, string value, Action? onToggle, Action? onMinus, Action? onPlus)[] rows,
            int contentX, int contentTop, int lineH, int visibleRows,
            int panelX, int panelY, int panelW, int panelH, Action? applySettings)
        {
            int totalRows = rows.Length;
            int maxOffset = Math.Max(0, totalRows - visibleRows);
            _tabScrollOffset = Math.Clamp(_tabScrollOffset, 0, maxOffset);
            int startIndex = _tabScrollOffset;
            int endExclusive = Math.Min(totalRows, startIndex + visibleRows);

            for (int i = startIndex; i < endExclusive; i++)
            {
                int rowY = contentTop + (i - startIndex) * lineH;
                var r = rows[i];

                if (r.onToggle == null && r.onMinus == null && r.onPlus == null)
                {
                    // Section header
                    _ui.DrawText(r.label, contentX, rowY, 20, new Vector4(0.75f, 0.9f, 1f, 1f));
                }
                else if (r.onToggle != null)
                {
                    DrawLabeledOption(r.label, r.value, contentX, rowY);
                    if (Button("Toggle", new Rect(contentX + 360, rowY - 4, 92, 32), 18))
                        r.onToggle();
                }
                else if (r.onMinus != null || r.onPlus != null)
                {
                    DrawLabeledOption(r.label, r.value, contentX, rowY);
                    if (r.onMinus != null && Button("-", new Rect(contentX + 360, rowY - 4, 42, 32), 22))
                        r.onMinus();
                    if (r.onPlus != null && Button("+", new Rect(contentX + 410, rowY - 4, 42, 32), 22))
                        r.onPlus();
                }
            }

            if (maxOffset > 0)
            {
                int scrollBarWidth = 10;
                int barX = panelX + panelW - 50;
                int barY = contentTop;
                int barH = visibleRows * lineH - 8;
                _ui.DrawRectangleLines(barX, barY, scrollBarWidth, barH, new Vector4(0.4f, 0.45f, 0.5f, 1f));
                float thumbRatio = visibleRows / (float)totalRows;
                int thumbH = Math.Max(18, (int)(barH * thumbRatio));
                int thumbTravel = Math.Max(0, barH - thumbH);
                int thumbY = barY + (thumbTravel == 0 ? 0 : (int)(thumbTravel * (_tabScrollOffset / (float)maxOffset)));
                _ui.DrawRectangle(barX + 1, thumbY + 1, scrollBarWidth - 2, Math.Max(1, thumbH - 2), new Vector4(0.55f, 0.75f, 0.95f, 1f));
            }
        }

        private void DrawLabeledOption(string label, string value, int x, int y)
        {
            _ui.DrawText(label, x, y, 22, Vector4.One);
            _ui.DrawText(value, x + 250, y, 22, new Vector4(0.75f, 0.9f, 1f, 1f));
        }

        private bool Button(string text, Rect rect, int fontSize = 28)
        {
            Vector2 mouse = _input.GetMousePosition();
            bool hover = mouse.X >= rect.X && mouse.X <= rect.X + rect.Width &&
                         mouse.Y >= rect.Y && mouse.Y <= rect.Y + rect.Height;
            bool click = hover && _uiLeftReleaseThisFrame && !_uiLeftReleaseConsumed;
            if (click)
                _uiLeftReleaseConsumed = true;

            Vector4 bg = hover
                ? new Vector4(60 / 255f, 70 / 255f, 85 / 255f, 1.0f)
                : new Vector4(40 / 255f, 46 / 255f, 56 / 255f, 1.0f);

            _ui.DrawRectangle((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, bg);
            _ui.DrawRectangleLines((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height,
                new Vector4(90 / 255f, 100 / 255f, 115 / 255f, 1.0f));

            int tw = _ui.MeasureText(text, fontSize);
            int tx = (int)rect.X + (int)rect.Width / 2 - tw / 2;
            int ty = (int)rect.Y + (int)rect.Height / 2 - fontSize / 2;
            _ui.DrawText(text, tx, ty, fontSize, Vector4.One);
            return click;
        }
    }
}
