using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Serilog;
using SkiaSharp;
using Svg.Skia;
using Veilborne.Core.Ecs.Components;
using Veilborne.Interfaces;

namespace Veilborne.Core.MonoGameImpl
{
    public class MonoGameGraphicsProvider : IGraphicsProvider
    {
        private readonly ILogger _logger = Log.ForContext<MonoGameGraphicsProvider>();
        private VeilborneGame? _game;
        private bool _initialized;
        private Color _clearColor = Color.Black;

        public int ScreenWidth => _game?.GraphicsDevice?.Viewport.Width ?? 1280;
        public int ScreenHeight => _game?.GraphicsDevice?.Viewport.Height ?? 720;

        public void InitializeWindow(int width, int height, string title)
        {
            if (_initialized) return;
            _game = new VeilborneGame(width, height, title);
            _game.Exiting += (s, e) => _initialized = false;
            _initialized = true;
        }

        public void SetUpdateCallback(Action<float> onUpdate)
        {
            if (_game != null) _game.OnUpdate = onUpdate;
        }

        public void Set3DDrawCallback(Action on3DDraw)
        {
            if (_game != null) _game.On3DDraw = on3DDraw;
        }

        public void Set2DDrawCallback(Action on2DDraw)
        {
            if (_game != null) _game.On2DDraw = on2DDraw;
        }

        // Legacy single-callback kept for compatibility
        public void SetDrawCallback(Action onDraw)
        {
            if (_game != null) _game.On2DDraw = onDraw;
        }

        public void SetLoadContentCallback(Action onLoadContent)
        {
            if (_game != null) _game.OnLoadContent = onLoadContent;
        }

        /// <summary>
        /// Starts the MonoGame game loop. Blocks until the game exits.
        /// Must be called from the thread that should own the window/graphics device.
        /// </summary>
        public void RunGameLoop()
        {
            _game?.Run();
        }

        public void CloseWindow() => _game?.RequestExit();
        public bool ShouldClose() => !_initialized;

        public void SetTargetFps(int fps)
        {
            if (_game != null)
                _game.TargetElapsedTime = TimeSpan.FromSeconds(1.0 / fps);
        }

        public float GetFrameTime() => _game != null ? (float)_game.TargetElapsedTime.TotalSeconds : 1f / 60f;
        public void SetWindowIcon(string relativeSvgPath)
        {
            if (_game == null)
            {
                _logger.Warning("Cannot set MonoGame window icon before window initialization.");
                return;
            }

            string svgPath = ResolveExistingPath(relativeSvgPath);
            if (!File.Exists(svgPath))
            {
                _logger.Warning("Window icon SVG not found at path: {IconPath}", relativeSvgPath);
                return;
            }

            const int iconSize = 256;
            using var svg = new SKSvg();
            using (var stream = File.OpenRead(svgPath))
            {
                svg.Load(stream);
            }

            if (svg.Picture == null)
            {
                _logger.Warning("Failed to parse SVG for window icon: {IconPath}", svgPath);
                return;
            }

            var bounds = svg.Picture.CullRect;
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                _logger.Warning("SVG has invalid bounds for window icon: {IconPath}", svgPath);
                return;
            }

            // First pass: fit SVG into square icon canvas.
            using var firstPass = new SKBitmap(iconSize, iconSize, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(firstPass))
            {
                canvas.Clear(SKColors.Transparent);
                float fitScale = MathF.Min(iconSize / bounds.Width, iconSize / bounds.Height);
                float drawW = bounds.Width * fitScale;
                float drawH = bounds.Height * fitScale;
                float offsetX = (iconSize - drawW) * 0.5f;
                float offsetY = (iconSize - drawH) * 0.5f;
                canvas.Translate(offsetX - bounds.Left * fitScale, offsetY - bounds.Top * fitScale);
                canvas.Scale(fitScale, fitScale);
                canvas.DrawPicture(svg.Picture);
                canvas.Flush();
            }

            // Second pass: crop transparent margins and re-center/fill with a small padding.
            int minX = iconSize, minY = iconSize, maxX = -1, maxY = -1;
            for (int y = 0; y < iconSize; y++)
            {
                for (int x = 0; x < iconSize; x++)
                {
                    if (firstPass.GetPixel(x, y).Alpha > 8)
                    {
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
            }

            using var bitmap = new SKBitmap(iconSize, iconSize, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.Transparent);

                if (maxX >= minX && maxY >= minY)
                {
                    var srcRect = new SKRectI(minX, minY, maxX + 1, maxY + 1);
                    float contentW = srcRect.Width;
                    float contentH = srcRect.Height;
                    const float paddingRatio = 0.08f;
                    float targetW = iconSize * (1f - paddingRatio * 2f);
                    float targetH = iconSize * (1f - paddingRatio * 2f);
                    float scale = MathF.Min(targetW / contentW, targetH / contentH);
                    float finalW = contentW * scale;
                    float finalH = contentH * scale;
                    float left = (iconSize - finalW) * 0.5f;
                    float top = (iconSize - finalH) * 0.5f;
                    var dstRect = new SKRect(left, top, left + finalW, top + finalH);
                    using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High };
                    canvas.DrawBitmap(firstPass, srcRect, dstRect, paint);
                }
                else
                {
                    canvas.DrawBitmap(firstPass, 0, 0);
                }
                canvas.Flush();
            }

            byte[] pixels = new byte[bitmap.ByteCount];
            Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);

            GCHandle pixelsHandle = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try
            {
                IntPtr surface = SDL_CreateRGBSurfaceFrom(
                    pixelsHandle.AddrOfPinnedObject(),
                    iconSize,
                    iconSize,
                    32,
                    iconSize * 4,
                    0x00FF0000,
                    0x0000FF00,
                    0x000000FF,
                    0xFF000000);

                if (surface == IntPtr.Zero)
                {
                    _logger.Warning("Failed creating SDL surface for window icon.");
                    return;
                }

                try
                {
                    SDL_SetWindowIcon(_game.Window.Handle, surface);
                }
                finally
                {
                    SDL_FreeSurface(surface);
                }
            }
            finally
            {
                pixelsHandle.Free();
            }
        }

        // No-ops: VeilborneGame.Draw manages the SpriteBatch lifecycle
        public void BeginDrawing() { }
        public void EndDrawing() { }

        // Stores the clear color; VeilborneGame.Draw clears before calling OnDraw
        public void Clear(System.Numerics.Vector3 color)
        {
            _clearColor = new Color(color.X, color.Y, color.Z);
            if (_game != null)
                _game.ClearColor = _clearColor;
        }

        public void ToggleBorderless()
        {
            if (_game != null)
                _game.Window.IsBorderless = !_game.Window.IsBorderless;
        }

        public void ToggleFullscreen()
        {
            _game?.ToggleFullscreen();
        }

        public void Begin3D(CameraComponent camera) { }
        public void End3D() { }
        public void DrawCube(System.Numerics.Vector3 position, System.Numerics.Vector3 size, System.Numerics.Vector3 color) { }
        public void DrawCubeWires(System.Numerics.Vector3 position, System.Numerics.Vector3 size, System.Numerics.Vector3 color) { }

        public GraphicsDevice? GetGraphicsDevice() => _game?.GraphicsDevice;
        public SpriteBatch? GetSpriteBatch() => _game?.SpriteBatch;
        public VeilborneGame? GetGame() => _game;

        private static string ResolveExistingPath(string relativePath)
        {
            var candidates = new[]
            {
                Path.GetFullPath(relativePath),
                Path.Combine(AppContext.BaseDirectory, relativePath),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", relativePath)),
                Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", relativePath))
            };

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return relativePath;
        }

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr SDL_CreateRGBSurfaceFrom(
            IntPtr pixels,
            int width,
            int height,
            int depth,
            int pitch,
            uint rmask,
            uint gmask,
            uint bmask,
            uint amask);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_SetWindowIcon(IntPtr window, IntPtr icon);

        [DllImport("SDL2.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void SDL_FreeSurface(IntPtr surface);
    }
}
