using Veilborne.GameWorlds;
using Veilborne.Interfaces;
using Veilborne.Systems.Core;
using Veilborne.Utility;

namespace Veilborne.Systems;

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
        foreach (var system in _systems.Where(s => s.Category != SystemCategory.Rendering))
        {
            if (time.State == EngineState.Running || system.RunsWhenPaused)
            {
                system.Update(time, state);
            }
        }
    }

    public void Render(GameTime time, GameState state)
    {
        foreach (var system in _systems.OfType<IRenderSystem>())
        {
            system.Render(time, state);
        }
    }

    public void ShutdownAll()
    {
        foreach (var system in _systems.Reverse<ISystem>())
        {
            system.Shutdown();
        }
    }
}
