using Veilborne.Core.GameWorlds;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Utility;

namespace Veilborne.Core.Systems;

public class SystemManager
{
    private readonly List<ISystem> _systems;

    public SystemManager(IEnumerable<ISystem> systems)
    {
        // Keep update order
        _systems = systems
            .OrderBy(s => s.Priority)
            .ToList();
    }

    public void InitializeAll()
    {
        foreach (var system in _systems)
        {
            system.Initialize();
        }
    }

    public void Update(GameTime time, GameState state)
    {
        foreach (var system in _systems.OfType<IUpdateSystem>())
        {
            if (time.State == EngineState.Running || system.RunsWhenPaused)
            {
                system.Update(time, state);
            }
        }
    }

    public void Render(GameTime time, GameState state)
    {
        foreach (var system in _systems.OfType<IRenderSystem>().OrderBy(s => s.Priority))
        {
            system.Render(time, state);
        }
    }

    public void ShutdownAll()
    {
        foreach (var system in _systems.AsEnumerable().Reverse())
        {
            system.Shutdown();
        }
    }
}
