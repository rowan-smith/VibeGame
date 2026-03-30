using Microsoft.Extensions.Hosting;
using Serilog;
using Veilborne.Core.Interfaces;

namespace Veilborne.Core
{
    public class Entry : IHostedService
    {
        private readonly ILogger logger = Log.ForContext<Entry>();
        private readonly IGameEngine _engine;
        private readonly IHostApplicationLifetime _appLifetime;

        public Entry(IGameEngine engine, IHostApplicationLifetime appLifetime)
        {
            _engine = engine;
            _appLifetime = appLifetime;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            logger.Information("Launching Veilborne (MonoGame)...");
            try
            {
                await _engine.RunAsync();
                logger.Information("3DEngine session ended");
            }
            catch (Exception ex)
            {
                logger.Error(ex, "3DEngine failed to run");
                throw;
            }
            finally
            {
                // Ensure the generic host stops once the game window closes
                _appLifetime.StopApplication();
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            // If there was a long-running engine or resources to dispose, handle it here.
            logger.Information("Stopping 3DEngine");
            return Task.CompletedTask;
        }
    }
}
