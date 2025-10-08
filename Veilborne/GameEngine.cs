using System.Diagnostics;
using Raylib_CsLo;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active;
using Veilborne.Utility;

namespace Veilborne;

public class GameEngine
{
    private readonly WorldManager _worldManager;
    private readonly Func<World> _worldFactory;

    private readonly Stopwatch _stopwatch = new Stopwatch();
    private bool _isRunning;
    private readonly GameTime _gameTime = new GameTime();

    private const int TargetFrameRate = 60;

    public GameEngine(WorldManager worldManager, Func<World> worldFactory)
    {
        _worldManager = worldManager;
        _worldFactory = worldFactory;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        _isRunning = true;

        // Initialize Raylib window and timing BEFORE any world/system initialization
        Raylib.SetConfigFlags(ConfigFlags.FLAG_VSYNC_HINT);
        Raylib.InitWindow(1280, 720, "Veilborne");
        Raylib.SetTargetFPS(TargetFrameRate);
        
        // Raylib.SetExitKey(KeyboardKey.KEY_NULL); // Disable default ESC-to-exit

        // Set window icon from SVG logo if available
        if (SvgTextureLoader.TryGetIconImage(Path.Combine("assets", "logo.svg"), 256, out var iconImg))
        {
            try
            {
                Raylib.SetWindowIcon(iconImg);
            }
            finally
            {
                Raylib.UnloadImage(iconImg);
            }
        }

        // Initialize worlds and systems after window is ready
        _worldManager.Initialize();

        var world = _worldFactory();
        _worldManager.AddWorld(world);

        _stopwatch.Start();
        double lastTime = _stopwatch.Elapsed.TotalSeconds;
        _gameTime.State = EngineState.Running;

        try
        {
            while (_isRunning && !cancellationToken.IsCancellationRequested)
            {
                // --- Input / Pause Handling ---
                if (Raylib.WindowShouldClose())
                {
                    _gameTime.State = EngineState.Exiting;
                    break;
                }

                double currentTime = _stopwatch.Elapsed.TotalSeconds;
                _gameTime.DeltaTime = (float)(currentTime - lastTime);
                _gameTime.TotalTime = (float)currentTime;
                lastTime = currentTime;

                // --- Game logic ---
                _worldManager.Update(_gameTime); // updates all systems

                // Rendering happens every frame, regardless of pause
                _worldManager.Render(_gameTime);
            }
        }
        catch (OperationCanceledException)
        {
            // Treat cancellation as a graceful shutdown
        }
        finally
        {
            Shutdown();
        }
    }

    private void Shutdown()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        _gameTime.State = EngineState.Exiting;
        _worldManager.Shutdown();
        _stopwatch.Stop();
        Raylib.CloseWindow();
    }
}
