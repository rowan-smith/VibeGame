using System;
using Microsoft.Extensions.Configuration;
using PoleCore.Logging;
using Serilog;

namespace Logging;

public sealed class VibeLogging : IDisposable
{
    private const string LogTemplate = "{Timestamp:dd-MM-yyyy HH:mm:ss} [{Level:u3}] {SourceContext}: {Message:lj} {Properties}{NewLine}{Exception}";
    
    private readonly ILogger _logger;
    private bool _disposed;

    public VibeLogging()
    {
        var configuration = LoggingUtils.LoadConfiguration();
        _logger = CreateLogger(configuration);
        Log.Logger = _logger;
    }

    private static ILogger CreateLogger(IConfiguration configuration)
    {
        var logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.WithDemystifiedStackTraces()
            .Enrich.WithCorrelationId()
            .WriteTo.Console(outputTemplate: LogTemplate)
            .CreateLogger();

        return logger;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Log.CloseAndFlush();
        if (_logger is IDisposable disposableLogger)
        {
            disposableLogger.Dispose();
        }

        _disposed = true;
    }
}
