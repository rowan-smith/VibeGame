using Microsoft.Extensions.DependencyInjection;
using Veilborne.Core;
using Veilborne.Core.GameWorlds;
using Veilborne.Core.Interfaces;
using Veilborne.Core.Systems.Core;
using Veilborne.Core.GameWorlds.Active;
using Veilborne.Windows.Input;
using Veilborne.Windows.Rendering;

namespace Veilborne.Windows;

public static class MonoGameServiceRegistration
{
    public static IServiceCollection AddWindowsGameServices(this IServiceCollection services)
    {
        // Platform providers
        services.AddSingleton<IInputProvider, KeyboardInputProvider>();
        services.AddSingleton<IMouseInput, MouseInputProvider>();

        // Rendering services
        services.AddSingleton<ICameraFactory, CameraFactory>();
        services.AddSingleton<IRenderer, GameRenderer>();

        services.AddSingleton<GameEngine>(sp =>
            new GameEngine(
                sp.GetRequiredService<WorldManager>(),
                sp.GetRequiredService<IRenderer>(),
                sp.GetRequiredService<Func<World>>()
            ));

        // Game host itself
        services.AddSingleton<VeilborneGame>();

        return services;
    }
}
