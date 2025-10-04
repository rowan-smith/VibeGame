using Microsoft.Extensions.Hosting;
using Serilog;

namespace VibeGame
{
    public class Entry : IHostedService
    {
        private readonly ILogger logger = Log.ForContext<Entry>();
        private readonly IGameEngine _engine;

        public Entry(IGameEngine engine)
        {
            _engine = engine;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            logger.Information("Launching 3DEngine (Raylib) first-person Corolla demo...");
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
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            logger.Information("Stopping 3DEngine");
        }
    }
}
