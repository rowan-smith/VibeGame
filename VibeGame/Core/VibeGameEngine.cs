using System.Numerics;
using Raylib_CsLo;
using VibeGame.Terrain;
using VibeGame.Core;
using VibeGame.Core.Items;

namespace VibeGame
{
    public class VibeGameEngine : IGameEngine
    {
        private readonly ICameraController _cameraController;
        private readonly IPhysicsController _physics;
        private readonly IInfiniteTerrain _infiniteTerrain;
        private readonly ITextureManager _textureManager;
        private readonly IItemRegistry _itemRegistry;
        private readonly int _startSeed;
        private readonly VibeGame.Core.World _world;

        private bool _debugVisible = false;
        private int _renderRadius = 4;

        // New: pause/exit menu and hotbar state
        private bool _isPaused = false;
        private bool _requestReturnToMenu = false;
        private bool _requestExitDesktop = false;
        private const int HotbarSlotCount = 8;
        private readonly List<HotbarItem> _hotbar = new();
        private int _selectedHotbar = 0;

        private sealed class HotbarItem
        {
            public string Name { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string IconPath { get; init; } = string.Empty;
            public string ModelPath { get; init; } = string.Empty;
        }

        // Cache for small UI-held models
        private readonly Dictionary<string, Model> _modelCache = new();

        public VibeGameEngine(ICameraController cameraController, IPhysicsController physics, IInfiniteTerrain infiniteTerrain, ITextureManager textureManager, IItemRegistry itemRegistry, VibeGame.Core.World world)
        {
            _cameraController = cameraController;
            _physics = physics;
            _infiniteTerrain = infiniteTerrain;
            _textureManager = textureManager;
            _itemRegistry = itemRegistry;
            _world = world;
            _startSeed = Environment.TickCount;
        }

        public async Task RunAsync()
        {
            Raylib.InitWindow(1280, 720, "Veilborne");
            Raylib.SetExitKey(KeyboardKey.KEY_NULL); // Disable default ESC-to-exit

            // Set window icon from SVG logo if available
            if (SvgTextureLoader.TryGetIconImage(Path.Combine("assets", "logo.svg"), 256, out var iconImg))
            {
                try { Raylib.SetWindowIcon(iconImg); } finally { Raylib.UnloadImage(iconImg); }
            }

            Raylib.SetTargetFPS(75);

            // Initialize hotbar items from assets/items (first slot shovel if available)
            InitializeHotbarFromAssets();

            bool assetsLoaded = false;
            bool appRunning = true;

            while (appRunning && !Raylib.WindowShouldClose())
            {
                // Ensure cursor visible for menu
                Raylib.EnableCursor();

                // Show main menu with Start button; returns false if user quits/escapes
                if (!ShowMainMenu())
                {
                    break;
                }

                // Loading screens
                if (!assetsLoaded)
                {
                    DrawSplashFrame("Veilborne", "Loading assets...");
                    await _textureManager.PreloadAsync();
                    assetsLoaded = true;
                }
                DrawSplashFrame("Veilborne", "Loading world...");

                // Basic FPS-style camera
                Camera3D camera = new Camera3D
                {
                    position = new Vector3(0.2f, 3.0f, -6f),
                    target = new Vector3(0.2f, 3.0f, -2f),
                    up = new Vector3(0, 1, 0),
                    fovy = 75f,
                    projection_ = CameraProjection.CAMERA_PERSPECTIVE,
                };

                // Choose a deterministic start location based on the world seed so the starting biome is random per seed
                static float Hash01(int v)
                {
                    unchecked
                    {
                        uint x = (uint)v;
                        x ^= 2747636419u;
                        x *= 2654435769u;
                        x ^= x >> 16;
                        x *= 2246822507u;
                        x ^= x >> 13;
                        x *= 3266489909u;
                        x ^= x >> 16;
                        return (x & 0xFFFFFF) / (float)0x1000000; // ~[0,1)
                    }
                }
                float startX = (Hash01(_startSeed ^ unchecked((int)0x9E3779B9u)) * 2400f) - 1200f;
                float startZ = (Hash01(_startSeed ^ unchecked((int)0x7F4A7C15u)) * 2400f) - 1200f;
                camera.position.X = startX;
                camera.position.Z = startZ;
                camera.target = new Vector3(startX + 0.2f, camera.position.Y, startZ + 4f);

                Color baseColor = new Color(182, 140, 102, 255);

                // Place camera on ground at start
                float startGround = _infiniteTerrain.SampleHeight(camera.position.X, camera.position.Z) + 1.7f;
                if (camera.position.Y < startGround)
                {
                    camera.position = new Vector3(camera.position.X, startGround, camera.position.Z);
                    camera.target.Y = camera.position.Y;
                }

                // Initialize world player position to camera start
                _world.Player.Position = camera.position;

                // Reset session state
                _isPaused = false;
                _requestReturnToMenu = false;
                _requestExitDesktop = false;

                // Capture mouse for FPS look once gameplay begins
                Raylib.DisableCursor();

                while (!Raylib.WindowShouldClose())
                {
                    float dt = Raylib.GetFrameTime();

                    // Toggle debug HUD with F1
                    if (Raylib.IsKeyPressed(KeyboardKey.KEY_F1))
                    {
                        _debugVisible = !_debugVisible;
                    }

                    // Toggle pause with ESC
                    if (Raylib.IsKeyPressed(KeyboardKey.KEY_ESCAPE))
                    {
                        _isPaused = !_isPaused;
                        if (_isPaused) Raylib.EnableCursor(); else Raylib.DisableCursor();
                    }

                    // Hotbar scroll selection via mouse wheel
                    float wheel = Raylib.GetMouseWheelMove();
                    if (MathF.Abs(wheel) > 0.01f)
                    {
                        int delta = wheel > 0 ? -1 : 1; // scroll up selects previous
                        _selectedHotbar = (_selectedHotbar + delta) % HotbarSlotCount;
                        if (_selectedHotbar < 0) _selectedHotbar += HotbarSlotCount;
                    }

                        // Sync world player position and update world systems
                    _world.Player.Position = camera.position;
                    _world.Update(camera.position);
                    _world.PumpAsyncJobs();

                    // Input-driven orientation + desired horizontal move
                    Vector3 horizMove = Vector3.Zero;
                    if (!_isPaused)
                    {
                        horizMove = _cameraController.UpdateAndGetHorizontalMove(ref camera, dt);
                    }

                    // Simple dig action: hold left mouse button to carve ahead of camera (only when not paused)
                    if (!_isPaused && _infiniteTerrain is IEditableTerrain editable2)
                    {
                        if (Raylib.IsMouseButtonDown(MouseButton.MOUSE_BUTTON_LEFT))
                        {
                            Vector3 dir = Vector3.Normalize(camera.target - camera.position);
                            Vector3 center = camera.position + dir * 3.5f;
                            _ = editable2.DigSphereAsync(center, radius: 2.0f, strength: 1.0f, falloff: VoxelFalloff.Cosine);
                        }
                    }

                    // Physics integration with ground sampling from infinite terrain
                    if (!_isPaused)
                    {
                        // Resolve collisions against nearby solid world objects (trees) in XZ plane
                        if (horizMove.X != 0f || horizMove.Z != 0f)
                        {
                            Vector2 startXZ = new Vector2(camera.position.X, camera.position.Z);
                            Vector2 targetXZ = new Vector2(camera.position.X + horizMove.X, camera.position.Z + horizMove.Z);
                            Vector2 correctedXZ = ResolveXZCollisions(startXZ, targetXZ);
                            horizMove = new Vector3(correctedXZ.X - startXZ.X, 0f, correctedXZ.Y - startXZ.Y);
                        }

                        _physics.Integrate(ref camera, dt, horizMove, (x, z) => _infiniteTerrain.SampleHeight(x, z));
                    }

                    // Safety clamp only when not paused to avoid camera snapping while in menu
                    if (!_isPaused)
                    {
                        float groundHere = _infiniteTerrain.SampleHeight(camera.position.X, camera.position.Z) + 1.7f;
                        if (camera.position.Y < groundHere)
                            camera.position = new Vector3(camera.position.X, groundHere, camera.position.Z);
                    }

                    Raylib.BeginDrawing();
                    Raylib.ClearBackground(Raylib.SKYBLUE);
                    Raylib.BeginMode3D(camera);

                    _infiniteTerrain.Render(camera, baseColor);

                    // Debug: draw chunk bounds when HUD is visible
                    if (_debugVisible)
                    {
                        _infiniteTerrain.RenderDebugChunkBounds(camera);
                    }

                    // Draw selected hotbar item as held 3D model in front of camera
                    DrawHeldItemInHand3D(camera);

                    Raylib.EndMode3D();

                    // Draw hotbar
                    DrawHotbar();

                    // 2D HUD overlay (debug)
                    if (_debugVisible)
                    {
                        int fps = Raylib.GetFPS();
                        var pos = camera.position;
                        float chunkWorld = (_infiniteTerrain.ChunkSize - 1) * _infiniteTerrain.TileSize;
                        int cx = (int)MathF.Floor(pos.X / chunkWorld);
                        int cz = (int)MathF.Floor(pos.Z / chunkWorld);
                        string biomeId = _infiniteTerrain.GetBiomeAt(pos.X, pos.Z).Data.DisplayName;

                        int x = 12;
                        int y = 12;
                        int w = 420;
                        int h = 140;
                        Raylib.DrawRectangle(x - 6, y - 6, w, h, new Color(24, 28, 36, 160));

                        Raylib.DrawText($"FPS: {fps}", x, y, 18, Raylib.LIME);
                        y += 22;
                        Raylib.DrawText($"dt: {dt:0.000}s", x, y, 18, Raylib.RAYWHITE);
                        y += 22;
                        Raylib.DrawText($"Pos: X={pos.X:0.0} Y={pos.Y:0.0} Z={pos.Z:0.0}", x, y, 18, Raylib.RAYWHITE);
                        y += 22;
                        Raylib.DrawText($"Chunk: ({cx}, {cz})  ChunkSize: {_infiniteTerrain.ChunkSize}  Tile: {_infiniteTerrain.TileSize:0.##}", x, y, 18, Raylib.RAYWHITE);
                        y += 22;
                        Raylib.DrawText($"Biome: {biomeId}", x, y, 18, Raylib.RAYWHITE);
                        y += 22;
                        Raylib.DrawText($"Render radius: {_renderRadius} chunks", x, y, 18, Raylib.RAYWHITE);
                    }

                    // Pause/Exit menu overlay
                    if (_isPaused)
                    {
                        DrawPauseMenu(out bool resumeClicked, out bool toMenuClicked, out bool toDesktopClicked);
                        if (resumeClicked)
                        {
                            _isPaused = false;
                            Raylib.DisableCursor();
                        }
                        if (toMenuClicked)
                        {
                            _requestReturnToMenu = true;
                        }
                        if (toDesktopClicked)
                        {
                            _requestExitDesktop = true;
                        }
                    }

                    Raylib.EndDrawing();

                    if (_requestExitDesktop || _requestReturnToMenu) break;
                }

                if (_requestExitDesktop)
                {
                    appRunning = false;
                }
                // else: return to menu on next outer-loop iteration
            }

            Raylib.CloseWindow();
        }

        private static void DrawSplashFrame(string title, string subtitle)
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();

            Raylib.BeginDrawing();
            // Subtle midnight gradient background to avoid harsh black
            Color top = new Color(10, 12, 16, 255);
            Color bottom = new Color(18, 22, 28, 255);
            Raylib.ClearBackground(top);
            Raylib.DrawRectangleGradientV(0, 0, w, h, top, bottom);

            // Subtle vignette top and bottom
            int vg = Math.Max(1, h / 5);
            Raylib.DrawRectangleGradientV(0, 0, w, vg, new Color(0, 0, 0, 80), new Color(0, 0, 0, 0));
            Raylib.DrawRectangleGradientV(0, h - vg, w, vg, new Color(0, 0, 0, 0), new Color(0, 0, 0, 120));

            // Try to draw splash SVG centered at a target area; scale at draw-time so size always updates
            float targetW = w * 0.995f;
            float targetH = h * 0.995f;
            // Request 2x raster size for crisp downscaling
            int reqW = Math.Max(1, (int)MathF.Ceiling(targetW * 2f));
            int reqH = Math.Max(1, (int)MathF.Ceiling(targetH * 2f));
            if (SvgTextureLoader.TryGetTexture(Path.Combine("assets", "spash.svg"), reqW, reqH, out var tex) && tex.id != 0)
            {
                float scale = MathF.Min(targetW / tex.width, targetH / tex.height);
                int drawW = (int)MathF.Round(tex.width * scale);
                int drawH = (int)MathF.Round(tex.height * scale);
                int x = (w - drawW) / 2;
                int y = (h - drawH) / 2;
                // Soft shadow behind artwork
                var src = new Rectangle(0, 0, tex.width, tex.height);
                var dstShadow = new Rectangle(x + 3, y + 3, drawW, drawH);
                var dst = new Rectangle(x, y, drawW, drawH);
                Raylib.DrawTexturePro(tex, src, dstShadow, new Vector2(0, 0), 0f, new Color(0, 0, 0, 100));
                Raylib.DrawTexturePro(tex, src, dst, new Vector2(0, 0), 0f, Raylib.RAYWHITE);
            }
            else
            {
                // Fallback simple text if SVG fails
                int titleSize = Math.Clamp(h / 10, 24, 64);
                int subSize = Math.Clamp(titleSize / 2, 16, 32);
                string t = title;
                string s = subtitle;
                int tW = Raylib.MeasureText(t, titleSize);
                int sW = Raylib.MeasureText(s, subSize);
                Raylib.DrawText(t, (w - tW) / 2, h / 2 - titleSize, titleSize, Raylib.RAYWHITE);
                Raylib.DrawText(s, (w - sW) / 2, h / 2 + 14, subSize, new Color(200, 200, 200, 255));
            }

            Raylib.EndDrawing();
        }

        private static bool ShowMainMenu()
        {
            bool start = false;

            while (!Raylib.WindowShouldClose() && !start)
            {
                int w = Raylib.GetScreenWidth();
                int h = Raylib.GetScreenHeight();
                int titleSize = Math.Clamp(h / 10, 24, 64);
                int subSize = Math.Clamp(titleSize / 2, 16, 32);

                Raylib.BeginDrawing();
                // Subtle midnight gradient background
                Color top = new Color(10, 12, 16, 255);
                Color bottom = new Color(18, 22, 28, 255);
                Raylib.ClearBackground(top);
                Raylib.DrawRectangleGradientV(0, 0, w, h, top, bottom);

                // Panel (soft slate, not pure black)
                int rectW = Math.Min(1100, (int)(w * 0.86f));
                int rectH = Math.Min(540, (int)(h * 0.70f));
                int rectX = (w - rectW) / 2;
                int rectY = (h - rectH) / 2;
                // outer soft shadow
                Raylib.DrawRectangleRounded(new Rectangle(rectX - 6, rectY - 6, rectW + 12, rectH + 12), 0.1f, 8, new Color(0, 0, 0, 60));
                Raylib.DrawRectangleRounded(new Rectangle(rectX, rectY, rectW, rectH), 0.08f, 8, new Color(24, 28, 36, 200));

                // Artwork at top: draw the full splash (including embedded title/tagline) and keep buttons below
                int centerX = w / 2;
                int y = rectY + 12;
                // Reserve the upper portion of the panel for the artwork
                float targetPanelW = rectW * 0.98f;
                float targetPanelH = rectH * 0.74f; // allow more height so text fits comfortably
                int reqMenuW = Math.Max(1, (int)MathF.Ceiling(targetPanelW * 2f));
                int reqMenuH = Math.Max(1, (int)MathF.Ceiling(targetPanelH * 2f));
                if (SvgTextureLoader.TryGetTexture(Path.Combine("assets", "spash.svg"), reqMenuW, reqMenuH, out var menuTex) && menuTex.id != 0)
                {
                    // Use the full image (no cropping) so the SVG's text is visible
                    int srcW = menuTex.width;
                    int srcH = menuTex.height;
                    var src = new Rectangle(0, 0, srcW, srcH);

                    float scale = MathF.Min(targetPanelW / srcW, targetPanelH / srcH);
                    int drawW = (int)MathF.Round(srcW * scale);
                    int drawH = (int)MathF.Round(srcH * scale);
                    int x = centerX - drawW / 2;

                    // soft shadow
                    var dstShadow = new Rectangle(x + 3, y + 3, drawW, drawH);
                    var dst = new Rectangle(x, y, drawW, drawH);
                    Raylib.DrawTexturePro(menuTex, src, dstShadow, new Vector2(0, 0), 0f, new Color(0, 0, 0, 100));
                    Raylib.DrawTexturePro(menuTex, src, dst, new Vector2(0, 0), 0f, Raylib.RAYWHITE);
                }
                // No text fallback — keep the area clean even if SVG fails

                // Start and Exit buttons (stacked vertically)
                int btnWidth = Math.Min(300, (int)(rectW * 0.5f));
                int btnHeight = Math.Max(48, subSize + 18);
                int btnGap = 12;
                int totalBtnH = btnHeight * 2 + btnGap;
                int buttonsTop = rectY + rectH - totalBtnH - 12;
                var startRect = new Rectangle(centerX - btnWidth / 2, buttonsTop, btnWidth, btnHeight);
                var exitRect = new Rectangle(centerX - btnWidth / 2, buttonsTop + btnHeight + btnGap, btnWidth, btnHeight);

                Vector2 mp = Raylib.GetMousePosition();
                bool hoverStart = Raylib.CheckCollisionPointRec(mp, startRect);
                bool hoverExit = Raylib.CheckCollisionPointRec(mp, exitRect);
                Color btnStartBg = hoverStart ? new Color(65, 130, 220, 255) : new Color(40, 90, 160, 255);
                Color btnExitBg = hoverExit ? new Color(200, 70, 70, 255) : new Color(160, 50, 50, 255);
                Raylib.DrawRectangleRounded(startRect, 0.12f, 8, btnStartBg);
                Raylib.DrawRectangleRounded(exitRect, 0.12f, 8, btnExitBg);

                const string startText = "Start";
                const string exitText = "Exit";
                int btnTextSize = Math.Clamp(subSize, 18, 28);
                int startTextWidth = Raylib.MeasureText(startText, btnTextSize);
                int exitTextWidth = Raylib.MeasureText(exitText, btnTextSize);
                Raylib.DrawText(startText, (int)(startRect.x + (btnWidth - startTextWidth) / 2), (int)(startRect.y + (btnHeight - btnTextSize) / 2), btnTextSize, Raylib.RAYWHITE);
                Raylib.DrawText(exitText, (int)(exitRect.x + (btnWidth - exitTextWidth) / 2), (int)(exitRect.y + (btnHeight - btnTextSize) / 2), btnTextSize, Raylib.RAYWHITE);


                Raylib.EndDrawing();

                if (hoverStart && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)) start = true;
                if (hoverExit && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)) return false;
                if (Raylib.IsKeyPressed(KeyboardKey.KEY_ENTER) || Raylib.IsKeyPressed(KeyboardKey.KEY_SPACE)) start = true;
                // Removed ESC-to-exit from the main menu
            }

            return start;
        }

        private void DrawHotbar()
        {
            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();
            int slots = HotbarSlotCount;
            int slotSize = Math.Clamp(w / (int)Math.Max(18, slots + 10), 40, 72);
            int gap = Math.Clamp(slotSize / 10, 4, 10);
            int totalW = slots * slotSize + (slots - 1) * gap;
            int x0 = (w - totalW) / 2;
            int y0 = h - slotSize - 14;

            // background panel
            Raylib.DrawRectangleRounded(new Rectangle(x0 - 12, y0 - 8, totalW + 24, slotSize + 16), 0.1f, 8, new Color(24, 28, 36, 160));

            for (int i = 0; i < slots; i++)
            {
                int x = x0 + i * (slotSize + gap);
                var rect = new Rectangle(x, y0, slotSize, slotSize);
                bool sel = i == _selectedHotbar;
                Color bg = sel ? new Color(70, 130, 180, 220) : new Color(40, 44, 54, 200);
                Raylib.DrawRectangleRounded(rect, 0.12f, 8, bg);

                // Draw item icon if present
                if (i < _hotbar.Count)
                {
                    var item = _hotbar[i];
                    if (!string.IsNullOrWhiteSpace(item.IconPath) && _textureManager.TryGetOrLoadByPath(item.IconPath, out var tex) && tex.id != 0)
                    {
                        float pad = MathF.Round(slotSize * 0.16f);
                        var dst = new Rectangle(x + pad, y0 + pad, slotSize - 2 * pad, slotSize - 2 * pad);

                        // keep aspect ratio
                        float aspect = (float)tex.width / Math.Max(1, tex.height);
                        if (aspect >= 1f)
                        {
                            float newH = dst.width / Math.Max(0.001f, aspect);
                            float y = y0 + (slotSize - newH) * 0.5f;
                            dst = new Rectangle(dst.x, y, dst.width, newH);
                        }
                        else
                        {
                            float newW = dst.height * aspect;
                            float xC = x + (slotSize - newW) * 0.5f;
                            dst = new Rectangle(xC, dst.y, newW, dst.height);
                        }

                        var src = new Rectangle(0, 0, tex.width, tex.height);
                        Raylib.DrawTexturePro(tex, src, dst, new System.Numerics.Vector2(0, 0), 0f, Raylib.RAYWHITE);
                    }
                }

                // index hint
                string idx = (i + 1).ToString();
                int idxSize = Math.Clamp(slotSize / 5 - 4, 12, 18);
                Raylib.DrawText(idx, x + 6, y0 + 4, idxSize, new Color(220, 220, 220, 220));

                // draw selection border
                if (sel)
                {
                    Raylib.DrawRectangleLinesEx(rect, 2f, new Color(255, 255, 255, 220));
                }
            }
        }

        private void InitializeHotbarFromAssets()
        {
            try
            {
                var items = _itemRegistry.All
                    .OrderBy(i => i.Id.Equals("shovel", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .Take(HotbarSlotCount)
                    .ToList();

                _hotbar.Clear();
                foreach (var it in items)
                {
                    _hotbar.Add(new HotbarItem
                    {
                        Name = it.Id,
                        DisplayName = it.DisplayName,
                        IconPath = it.IconPath,
                        ModelPath = it.ModelPath
                    });
                }

                _selectedHotbar = 0;
            }
            catch
            {
                // ignore any errors during discovery; leave hotbar empty
            }
        }

        private static string MakeRelativeIfPossible(string path)
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                var rel = Path.GetRelativePath(cwd, path);
                if (!rel.StartsWith("..")) return rel.Replace('/', '\\');
            }
            catch { }
            return path;
        }

        // Resolve simple horizontal collisions against solid world object colliders (treated as XZ circles)
        private Vector2 ResolveXZCollisions(Vector2 start, Vector2 target)
        {
            const float playerRadius = 0.3f;
            Vector2 pos = target;
            float range = MathF.Max(2f, Vector2.Distance(start, target) + 2f);
            IEnumerable<(Vector2 center, float radius)> colliders;
            try { colliders = _infiniteTerrain.GetNearbyObjectColliders(pos, range); }
            catch { colliders = Array.Empty<(Vector2, float)>(); }
            if (colliders == null) return pos;

            // Iterate a couple of times to resolve multiple overlaps
            for (int iter = 0; iter < 2; iter++)
            {
                bool adjusted = false;
                foreach (var c in colliders)
                {
                    float r = c.radius + playerRadius;
                    Vector2 d = pos - c.center;
                    float d2 = d.X * d.X + d.Y * d.Y;
                    float r2 = r * r;
                    if (d2 < r2)
                    {
                        if (d2 < 1e-6f)
                        {
                            pos = c.center + new Vector2(r, 0f);
                        }
                        else
                        {
                            float dist = MathF.Sqrt(d2);
                            Vector2 pushDir = d / MathF.Max(dist, 1e-4f);
                            pos = c.center + pushDir * r;
                        }
                        adjusted = true;
                    }
                }
                if (!adjusted) break;
            }
            return pos;
        }


        private void DrawHeldItemInHand3D(Camera3D camera)
        {
            // Don't render held item when paused (keeps menus clean)
            if (_isPaused) return;
            if (_hotbar.Count == 0) return;
            int sel = Math.Clamp(_selectedHotbar, 0, HotbarSlotCount - 1);
            if (sel >= _hotbar.Count) return;
            var item = _hotbar[sel];
            if (string.IsNullOrWhiteSpace(item.ModelPath)) return;

            // Load or get cached model
            if (!_modelCache.TryGetValue(item.ModelPath, out var model))
            {
                try
                {
                    model = Raylib.LoadModel(item.ModelPath);
                    if (model.meshCount > 0)
                    {
                        _modelCache[item.ModelPath] = model;
                    }
                }
                catch
                {
                    return;
                }
            }

            // Camera basis vectors
            var forward = Vector3.Normalize(camera.target - camera.position);
            var up = Vector3.Normalize(camera.up);
            var right = Vector3.Normalize(Vector3.Cross(forward, up));

            // Hand placement offsets (tuned for a first-person "ready" pose)
            float distForward = 0.50f;
            float offsetRight = 0.50f;
            float offsetDown = 0.24f; // down relative to camera up

            // Subtle idle sway so it feels attached to the player instead of floating
            double t = Raylib.GetTime();
            float swayX = (float)Math.Sin(t * 7.0) * 0.02f;   // side sway
            float swayY = (float)Math.Cos(t * 11.0) * 0.02f;  // vertical sway

            var worldPos = camera.position
                          + forward * distForward
                          + right * (offsetRight + swayX)
                          - up * (offsetDown + MathF.Abs(swayY) * 0.5f);

            // Determine scale so model fits nicely as a handheld object
            var bbox = Raylib.GetModelBoundingBox(model);
            var ext = bbox.max - bbox.min;
            float maxExtent = MathF.Max(ext.X, MathF.Max(ext.Y, ext.Z));
            if (maxExtent <= 0.0001f) maxExtent = 1f;
            float targetSize = 0.65f; // desired approximate size in world units
            float scale = targetSize / maxExtent;

            // Center the model so it pivots more naturally in hand
            var center = (bbox.min + bbox.max) * 0.5f;
            var localOffset = -center * scale;

            // Build camera orientation quaternion from basis vectors
            var camMat = new Matrix4x4(
                right.X,   right.Y,   right.Z,   0f,
                up.X,      up.Y,      up.Z,      0f,
                -forward.X,-forward.Y,-forward.Z,0f,
                0f,        0f,        0f,        1f);
            var qCam = Quaternion.CreateFromRotationMatrix(camMat);

            // Hand-held offset rotation (how the hand grips the item)
            var qOffset = Quaternion.CreateFromYawPitchRoll(
                MathF.PI / 180f * -12f, // yaw (slight left)
                MathF.PI / 180f * -55f, // pitch (more forward/down — ready to use)
                MathF.PI / 180f * 22f   // roll (toward screen corner)
            );
            // Flip the shovel 180° around local Y so it faces the other way
            if (string.Equals(item.Name, "shovel", StringComparison.OrdinalIgnoreCase))
            {
                var qFlip = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), MathF.PI);
                qOffset = qOffset * qFlip;
            }

            // Tiny bobbing rotation
            var qBob = Quaternion.CreateFromAxisAngle(new Vector3(0, 1, 0), (float)Math.Sin(t * 5.0) * (MathF.PI / 180f * 3f));

            // Final orientation
            var q = Quaternion.Normalize(qOffset * qBob * qCam);

            // Convert quaternion to axis-angle for DrawModelEx
            Vector3 axis = new Vector3(q.X, q.Y, q.Z);
            float axisLen = axis.Length();
            float angleDeg;
            if (axisLen > 1e-6f)
            {
                axis /= axisLen;
                float w = q.W;
                if (w > 1f) w = 1f; else if (w < -1f) w = -1f;
                float angle = 2f * MathF.Acos(w);
                angleDeg = angle * (180f / MathF.PI);
            }
            else
            {
                axis = new Vector3(0, 1, 0);
                angleDeg = 0f;
            }

            Raylib.DrawModelEx(model, worldPos + localOffset, axis, angleDeg, new Vector3(scale, scale, scale), Raylib.WHITE);
        }

        private void DrawPauseMenu(out bool resumeClicked, out bool exitToMenuClicked, out bool exitToDesktopClicked)
        {
            resumeClicked = false;
            exitToMenuClicked = false;
            exitToDesktopClicked = false;

            int w = Raylib.GetScreenWidth();
            int h = Raylib.GetScreenHeight();

            // dim overlay
            Raylib.DrawRectangle(0, 0, w, h, new Color(10, 12, 16, 180));

            int rectW = Math.Min(520, (int)(w * 0.7f));
            int rectH = Math.Min(360, (int)(h * 0.6f));
            int rectX = (w - rectW) / 2;
            int rectY = (h - rectH) / 2;
            Raylib.DrawRectangleRounded(new Rectangle(rectX, rectY, rectW, rectH), 0.08f, 8, new Color(24, 28, 36, 220));

            string title = "Paused";
            int titleSize = Math.Clamp(h / 14, 22, 46);
            int titleWidth = Raylib.MeasureText(title, titleSize);
            Raylib.DrawText(title, rectX + (rectW - titleWidth) / 2, rectY + 22, titleSize, Raylib.RAYWHITE);

            int btnWidth = Math.Min(260, (int)(w * 0.35f));
            int btnHeight = Math.Max(44, titleSize / 2 + 12);
            int centerX = w / 2;
            int gap = 10;

            int totalButtonsHeight = btnHeight * 3 + gap * 2;
            int baseY = rectY + (rectH - totalButtonsHeight) / 2 + 8;

            var resumeRect = new Rectangle(centerX - btnWidth / 2, baseY, btnWidth, btnHeight);
            var menuRect = new Rectangle(centerX - btnWidth / 2, baseY + btnHeight + gap, btnWidth, btnHeight);
            var desktopRect = new Rectangle(centerX - btnWidth / 2, baseY + (btnHeight + gap) * 2, btnWidth, btnHeight);

            Vector2 mp = Raylib.GetMousePosition();
            bool hoverResume = Raylib.CheckCollisionPointRec(mp, resumeRect);
            bool hoverMenu = Raylib.CheckCollisionPointRec(mp, menuRect);
            bool hoverDesktop = Raylib.CheckCollisionPointRec(mp, desktopRect);

            Color btnResume = hoverResume ? new Color(65, 130, 220, 255) : new Color(40, 90, 160, 255);
            Color btnMenu = hoverMenu ? new Color(200, 150, 70, 255) : new Color(160, 120, 50, 255);
            Color btnDesktop = hoverDesktop ? new Color(200, 70, 70, 255) : new Color(160, 50, 50, 255);

            Raylib.DrawRectangleRounded(resumeRect, 0.12f, 8, btnResume);
            Raylib.DrawRectangleRounded(menuRect, 0.12f, 8, btnMenu);
            Raylib.DrawRectangleRounded(desktopRect, 0.12f, 8, btnDesktop);

            int fs = Math.Clamp(titleSize / 2, 16, 28);
            int tw1 = Raylib.MeasureText("Resume", fs);
            Raylib.DrawText("Resume", (int)(resumeRect.x + (btnWidth - tw1) / 2), (int)(resumeRect.y + (btnHeight - fs) / 2), fs, Raylib.RAYWHITE);
            int tw2 = Raylib.MeasureText("Exit to Menu", fs);
            Raylib.DrawText("Exit to Menu", (int)(menuRect.x + (btnWidth - tw2) / 2), (int)(menuRect.y + (btnHeight - fs) / 2), fs, Raylib.RAYWHITE);
            int tw3 = Raylib.MeasureText("Exit to Desktop", fs);
            Raylib.DrawText("Exit to Desktop", (int)(desktopRect.x + (btnWidth - tw3) / 2), (int)(desktopRect.y + (btnHeight - fs) / 2), fs, Raylib.RAYWHITE);

            if (hoverResume && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)) resumeClicked = true;
            if (hoverMenu && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)) exitToMenuClicked = true;
            if (hoverDesktop && Raylib.IsMouseButtonPressed(MouseButton.MOUSE_BUTTON_LEFT)) exitToDesktopClicked = true;

            // keyboard shortcuts
            // Do not handle ESC here to avoid immediate re-toggle; ESC toggling is handled in the main loop.
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_M)) exitToMenuClicked = true;
            if (Raylib.IsKeyPressed(KeyboardKey.KEY_X)) exitToDesktopClicked = true;
        }
    }
}
