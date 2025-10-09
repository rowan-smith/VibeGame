using System.Diagnostics;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.GameWorlds.Active;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Utility;

namespace Veilborne.Core;

public class GameEngine
{
    private readonly WorldManager _worldManager;
    private readonly IRenderer _renderer;
    private readonly Func<World> _worldFactory;
    private readonly GameTime _gameTime = new GameTime();

    private readonly Stopwatch _stopwatch = new Stopwatch();

    public GameEngine(WorldManager worldManager, IRenderer renderer, Func<World> worldFactory)
    {
        _worldManager = worldManager;
        _renderer = renderer;
        _worldFactory = worldFactory;
    }

    public void Initialize()
    {
        _renderer.InitWindow(1280, 720, "Veilborne");
        // Ensure at least one world exists
        var initialWorld = _worldFactory();
        _worldManager.AddWorld(initialWorld);
        _worldManager.Initialize();
        _stopwatch.Start();
        // Start in running state so movement works without manual toggle
        _gameTime.State = EngineState.Running;
    }

    public void Update(float deltaTime)
    {
        _gameTime.DeltaTime = deltaTime;
        _gameTime.TotalTime += deltaTime;
        _worldManager.Update(_gameTime);
    }

    public void Draw()
    {
        _renderer.Clear(System.Drawing.Color.CornflowerBlue);
        _worldManager.Render(_gameTime);
    }

    public void Shutdown()
    {
        _worldManager.Shutdown();
        _stopwatch.Stop();
        _renderer.CloseWindow();
    }

    public EngineState GetEngineState()
    {
        return _gameTime.State;
    }
}
