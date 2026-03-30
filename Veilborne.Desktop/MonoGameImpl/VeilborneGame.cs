using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace Veilborne.Desktop.MonoGameImpl
{
    public class VeilborneGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch? _spriteBatch;

        public SpriteBatch? SpriteBatch => _spriteBatch;

        // Callbacks set by the engine
        public Action? OnLoadContent;
        public Action<float>? OnUpdate;
        public Action? On3DDraw;   // Before SpriteBatch - for 3D geometry (terrain, world objects)
        public Action? On2DDraw;   // Inside SpriteBatch - for UI / HUD
        public Action? OnPostDraw; // After everything - snapshot input state here

        private bool _shouldExit;
        private bool _screenshotRequested;
        private static int _screenshotWriteInFlight;

        /// <summary>When true, mouse is captured for FPS-style look.</summary>
        public bool MouseLocked { get; set; } = false;
        public bool IsWindowActive { get; private set; } = true;

        public VeilborneGame(int width, int height, string title)
        {
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = width;
            _graphics.PreferredBackBufferHeight = height;
            _graphics.HardwareModeSwitch = true; // prefer true fullscreen over borderless fake fullscreen
            _graphics.SynchronizeWithVerticalRetrace = true;
            Content.RootDirectory = "Content";
            IsFixedTimeStep = true;
            IsMouseVisible = true;
            Window.Title = title;
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(GraphicsDevice);
            OnLoadContent?.Invoke();
        }

        protected override void Update(GameTime gameTime)
        {
            if (_shouldExit) { Exit(); return; }
            IsWindowActive = IsActive;

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            OnUpdate?.Invoke(dt);
            base.Update(gameTime);
        }

        public XnaColor ClearColor { get; set; } = XnaColor.Black;

        protected override void Draw(GameTime gameTime)
        {
            bool captureThisFrame = _screenshotRequested;

            GraphicsDevice.Clear(ClearColor);
            On3DDraw?.Invoke();
            if (_spriteBatch != null)
            {
                _spriteBatch.Begin();
                On2DDraw?.Invoke();
                _spriteBatch.End();
            }

            OnPostDraw?.Invoke();

            if (captureThisFrame)
            {
                _screenshotRequested = false;
                CaptureScreenshotFromBackBuffer(GraphicsDevice);
            }

            base.Draw(gameTime);
        }

        public void RequestExit() => _shouldExit = true;

        public void RequestScreenshot() => _screenshotRequested = true;

        public void ToggleFullscreen()
        {
            Window.IsBorderless = false;
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
        }

        protected override void UnloadContent()
        {
            base.UnloadContent();
        }

        private static void CaptureScreenshotFromBackBuffer(GraphicsDevice graphicsDevice)
        {
            int width = graphicsDevice.PresentationParameters.BackBufferWidth;
            int height = graphicsDevice.PresentationParameters.BackBufferHeight;
            if (width <= 0 || height <= 0)
                return;

            // Keep frame-time stable if users spam capture; drop while a write is in-flight.
            if (Interlocked.CompareExchange(ref _screenshotWriteInFlight, 1, 0) != 0)
                return;

            var pixels = new XnaColor[width * height];
            graphicsDevice.GetBackBufferData(pixels);

            string screenshotDir = ResolveScreenshotDirectory();
            Directory.CreateDirectory(screenshotDir);
            string outputPath = Path.Combine(screenshotDir, $"veilborne_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");

            _ = Task.Run(() =>
            {
                try
                {
                    using var image = new Image<Rgba32>(width, height);
                    image.ProcessPixelRows(accessor =>
                    {
                        for (int y = 0; y < height; y++)
                        {
                            int rowStart = y * width;
                            var row = accessor.GetRowSpan(y);
                            for (int x = 0; x < width; x++)
                            {
                                var c = pixels[rowStart + x];
                                // Force opaque alpha so screenshots represent final on-screen colors
                                // regardless of backbuffer alpha semantics.
                                row[x] = new Rgba32(c.R, c.G, c.B, 255);
                            }
                        }
                    });

                    using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    image.Save(fs, new PngEncoder { CompressionLevel = PngCompressionLevel.BestSpeed });
                }
                finally
                {
                    Interlocked.Exchange(ref _screenshotWriteInFlight, 0);
                }
            });
        }

        private static string ResolveScreenshotDirectory()
        {
            const string gameName = "Veilborne";
            string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!string.IsNullOrWhiteSpace(docs))
                return Path.Combine(docs, "My Games", gameName, "Screenshots");

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
                return Path.Combine(localAppData, gameName, "Screenshots");

            return Path.Combine(AppContext.BaseDirectory, "screenshots");
        }
    }
}
