using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;

namespace Veilborne.Core.MonoGameImpl
{
    public class VeilborneGame : Game
    {
        private GraphicsDeviceManager _graphics;
        private SpriteBatch? _spriteBatch;

        public SpriteBatch? SpriteBatch => _spriteBatch;

        // Callbacks set by the engine
        public Action? OnLoadContent;
        public Action<float>? OnUpdate;
        public Action? On3DDraw;   // Before SpriteBatch — for 3D geometry (terrain, world objects)
        public Action? On2DDraw;   // Inside SpriteBatch — for UI / HUD
        public Action? OnPostDraw; // After everything — snapshot input state here

        private bool _shouldExit;

        /// <summary>When true, mouse is captured for FPS-style look.</summary>
        public bool MouseLocked { get; set; } = false;

        public VeilborneGame(int width, int height, string title)
        {
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = width;
            _graphics.PreferredBackBufferHeight = height;
            _graphics.HardwareModeSwitch = true; // prefer true fullscreen over borderless fake fullscreen
            Content.RootDirectory = "Content";
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

            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            OnUpdate?.Invoke(dt);
            base.Update(gameTime);
        }

        public Color ClearColor { get; set; } = Color.Black;

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(ClearColor);
            On3DDraw?.Invoke();  // Terrain/world — depth test enabled, before SpriteBatch messes with states
            if (_spriteBatch != null)
            {
                _spriteBatch.Begin();
                On2DDraw?.Invoke();  // HUD/UI — inside SpriteBatch
                _spriteBatch.End();
            }
            OnPostDraw?.Invoke();
            base.Draw(gameTime);
        }

        public void RequestExit() => _shouldExit = true;

        public void ToggleFullscreen()
        {
            Window.IsBorderless = false;
            _graphics.IsFullScreen = !_graphics.IsFullScreen;
            _graphics.ApplyChanges();
        }
    }
}
