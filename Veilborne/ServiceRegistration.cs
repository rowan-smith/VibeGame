using Microsoft.Extensions.DependencyInjection;
using Veilborne.GameWorlds;
using Veilborne.GameWorlds.Active;
using Veilborne.GameWorlds.Active.Entities;
using Veilborne.Interfaces;
using Veilborne.Systems;
using Veilborne.Systems.Core;

namespace Veilborne;

public static class ServiceRegistration
{
    public static void RegisterGameServices(this IServiceCollection services)
    {
        services.AddSingleton<Player>();

        services.AddSingleton<WorldManager>();

        services.AddSingleton<SystemManager>();
        services.AddSingleton<GameEngine>();

        services.AddTransient<Func<Player>>(_ => () => new Player());

        services.AddTransient<Func<World>>(sp => () =>
        {
            var player = sp.GetRequiredService<Func<Player>>()();
            var state = new GameState(player);

            var worldSystems = new List<ISystem>
            {
                new PhysicsSystem(),
                new RenderSystem(),
                new CameraSystem(),
            };

            return new World(state, worldSystems);
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
