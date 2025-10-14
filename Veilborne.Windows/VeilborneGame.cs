using Microsoft.Xna.Framework;
using Veilborne.Core;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Utility;
using Veilborne.Windows.Rendering;
using GameTime = Microsoft.Xna.Framework.GameTime;

namespace Veilborne.Windows;

public class VeilborneGame : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private readonly GameEngine _engine;
    private readonly IRenderer _renderer;
    private readonly IInputProvider _input;

    public VeilborneGame(GameEngine engine, IRenderer renderer, IInputProvider input)
    {
        _graphics = new GraphicsDeviceManager(this);
        Content.RootDirectory = "Content";
        IsMouseVisible = false;

        _engine = engine;
        _renderer = renderer;
        _input = input;

        // Attach the GraphicsDevice to the renderer before engine init
        if (_renderer is GameRenderer gr)
        {
            gr.AttachGraphicsManager(_graphics);
            gr.AttachWindow(Window);
        }
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
        // Refresh platform input state once per frame
        _input.Update();

        _engine.Update((float)gameTime.ElapsedGameTime.TotalSeconds);

        if (_engine.GetEngineState() == EngineState.Exiting)
        {
            Exit(); // <-- actually tells MonoGame to exit
        }

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
