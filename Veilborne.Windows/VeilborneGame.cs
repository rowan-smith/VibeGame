using Microsoft.Xna.Framework;
using Veilborne.Core;
using Veilborne.Core.Interfaces;
using Veilborne.Windows.Rendering;

namespace Veilborne.Windows;

public class VeilborneGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly GameEngine _engine;
    private readonly IRenderer _renderer;

    public VeilborneGame(GameEngine engine, IRenderer renderer)
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = true;

        _engine = engine;
        _renderer = renderer;
    }

    protected override void Initialize()
    {
        // Attach the GraphicsDevice to the renderer before engine init
        if (_renderer is GameRenderer gr)
        {
            gr.Attach(GraphicsDevice);
        }

        _engine.Initialize();
        base.Initialize();
    }

    protected override void Update(GameTime gameTime)
    {
        _engine.Update((float)gameTime.ElapsedGameTime.TotalSeconds);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        _engine.Draw();
        base.Draw(gameTime);
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        _engine.Shutdown();
        base.OnExiting(sender, args);
    }
}
