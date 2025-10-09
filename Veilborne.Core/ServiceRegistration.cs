using Microsoft.Extensions.DependencyInjection;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Systems;
using Veilborne.Core.Systems.Core;
using Veilborne.Core.GameWorlds.Active;
using Veilborne.Core.GameWorlds.Active.Entities;

namespace Veilborne.Core;

public static class ServiceRegistration
{
    public static void AddGameServices(this IServiceCollection services)
    {
        // Entities and world lifecycle
        services.AddTransient<Func<Player>>(_ => () => new Player());
        services.AddSingleton<WorldManager>();

        // Factory to create a world that uses DI-managed systems
        services.AddTransient<Func<World>>(sp => () =>
        {
            var player = sp.GetRequiredService<Func<Player>>()();
            var state = new GameState(player);

            var systems = sp.GetRequiredService<IEnumerable<ISystem>>();
            return new World(state, systems);
        });

        services.RegisterShared();
        services.RegisterSystems();
    }

    private static void RegisterSystems(this IServiceCollection services)
    {
        // Automatically register all ISystem implementations as singletons
        // This ensures systems that depend on other systems (e.g., RenderSystem -> CameraSystem)
        // receive the same instances that are updated by the SystemManager each frame.
        services.Scan(scan => scan
            .FromAssembliesOf(typeof(ISystem))
            .AddClasses(c => c.AssignableTo<ISystem>())
            .AsSelfWithInterfaces()
            .WithSingletonLifetime());
    }

    private static void RegisterShared(this IServiceCollection services)
    {
        // Shared services
        // services.AddSingleton<NoiseService>();
        // services.AddSingleton<BiomeService>();
        // services.AddSingleton<ConfigService>();
    }
}
