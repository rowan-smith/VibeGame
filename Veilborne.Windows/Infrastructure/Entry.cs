using Microsoft.Extensions.Hosting;
using Serilog;
using Veilborne.Windows;

namespace Veilborne.Core.Infrastructure;

public class Entry : IHostedService
{
    private readonly ILogger _logger = Log.ForContext<Entry>();
    private readonly VeilborneGame _game;

    public Entry(VeilborneGame game)
    {
        _game = game;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.Information("Launching Veilborne...");

        // Runs the MonoGame loop on the main thread
        _game.Run();

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.Information("Stopping Veilborne");
        return Task.CompletedTask;
    }
}
