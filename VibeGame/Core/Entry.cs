using Microsoft.Extensions.Hosting;
using Serilog;
using VibeGame.Core;

namespace VibeGame
{
    public class Entry : IHostedService
    {
        private readonly ILogger logger = Log.ForContext<Entry>();
        private readonly IGameEngine _engine;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ITextureManager _textureManager;

        public Entry(IGameEngine engine, IHostApplicationLifetime appLifetime, ITextureManager textureManager)
        {
            _engine = engine;
            _appLifetime = appLifetime;
            _textureManager = textureManager;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            logger.Information("Launching VibeGame (Raylib)...");

            // Install Raylib -> Serilog log bridge so all Raylib output goes through our logger
            RaylibLogBridge.Install();
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
