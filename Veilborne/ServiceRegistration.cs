using Microsoft.Extensions.DependencyInjection;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active;
using Veilborne.GameWorlds.Active.Entities;
using Veilborne.Interfaces;
using Veilborne.Systems;

namespace Veilborne;

public static class ServiceRegistration
{
    public static void RegisterGameServices(this IServiceCollection services)
    {
        services.AddSingleton<Player>();

        services.AddSingleton<WorldManager>();

        services.AddSingleton<SystemManager>();
        services.AddSingleton<GameEngine>();

        services.AddTransient<Func<World>>(sp => () =>
        {
            var systems = sp.GetServices<ISystem>().ToList();
            var player = sp.GetRequiredService<Player>();
            var state = new GameState(player);
            return new World(state, systems); // now passing both arguments
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
