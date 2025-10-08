using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Raylib_CsLo;
using Serilog;
using Serilog.Events;

namespace Veilborne.Utility;

/// <summary>
/// Bridges raylib internal logging into Serilog so all engine logs go through the same sink.
/// </summary>
public static class RaylibLogBridge
{
    private static bool _installed;
    private static readonly ILogger Logger = Log.ForContext("SourceContext", "Raylib");

    public static unsafe void Install()
    {
        if (_installed)
        {
            return;
        }

        try
        {
            // Route all raylib logs to our callback
            Raylib.SetTraceLogCallback(&TraceCallback);

            // Let raylib emit all levels; filtering is done by Serilog configuration
            Raylib.SetTraceLogLevel((int)TraceLogLevel.LOG_ALL);

            _installed = true;
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to install Raylib log bridge");
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static unsafe void TraceCallback(int logLevel, sbyte* text, sbyte* args)
    {
        var message = Marshal.PtrToStringUTF8((IntPtr)text) ?? string.Empty;

        var seriLogLevel = MapLevel((TraceLogLevel)logLevel);
        Logger.Write(seriLogLevel, "{RaylibMessage}", message);
    }

    private static LogEventLevel MapLevel(TraceLogLevel level) => level switch
    {
        TraceLogLevel.LOG_TRACE or TraceLogLevel.LOG_DEBUG => LogEventLevel.Debug,
        TraceLogLevel.LOG_INFO => LogEventLevel.Information,
        TraceLogLevel.LOG_WARNING => LogEventLevel.Warning,
        TraceLogLevel.LOG_ERROR => LogEventLevel.Error,
        TraceLogLevel.LOG_FATAL => LogEventLevel.Fatal,
        _ => LogEventLevel.Information,
    };
}


// Mirror raylib TraceLogLevel enum (Raylib-CsLo exposes constants with the same names)
internal enum TraceLogLevel
{
    LOG_ALL = 0,
    LOG_TRACE,
    LOG_DEBUG,
    LOG_INFO,
    LOG_WARNING,
    LOG_ERROR,
    LOG_FATAL,
    LOG_NONE
}
