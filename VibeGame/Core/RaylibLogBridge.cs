using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Serilog;
using Raylib_CsLo;

namespace VibeGame.Core
{
    /// <summary>
    /// Bridges raylib internal logging into Serilog so all engine logs go through the same sink.
    /// </summary>
    public static class RaylibLogBridge
    {
        private static bool _installed;
        private static readonly ILogger Logger = Log.ForContext("SourceContext", "Raylib");

        public static unsafe void Install()
        {
            if (_installed) return;

            try
            {
                // Route all raylib logs to our callback
                Raylib.SetTraceLogCallback((delegate* unmanaged[Cdecl]<int, sbyte*, sbyte*, void>)&TraceCallback);
                // Let raylib emit all levels; filtering is done by Serilog configuration
                Raylib.SetTraceLogLevel((int)TraceLogLevel.LOG_ALL);
                _installed = true;
            }
            catch (Exception ex)
            {
                Log.ForContext(typeof(RaylibLogBridge)).Warning(ex, "Failed to install Raylib log bridge");
            }
        }

        [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
        private static unsafe void TraceCallback(int logLevel, sbyte* text, sbyte* args)
        {
            string message = Marshal.PtrToStringUTF8((IntPtr)text) ?? string.Empty;

            // Map raylib levels to Serilog levels
            switch ((TraceLogLevel)logLevel)
            {
                case TraceLogLevel.LOG_TRACE:
                case TraceLogLevel.LOG_DEBUG:
                    Logger.Debug("{RaylibMessage}", message);
                    break;
                case TraceLogLevel.LOG_INFO:
                    Logger.Information("{RaylibMessage}", message);
                    break;
                case TraceLogLevel.LOG_WARNING:
                    Logger.Warning("{RaylibMessage}", message);
                    break;
                case TraceLogLevel.LOG_ERROR:
                    Logger.Error("{RaylibMessage}", message);
                    break;
                case TraceLogLevel.LOG_FATAL:
                    Logger.Fatal("{RaylibMessage}", message);
                    break;
                default:
                    Logger.Information("{RaylibMessage}", message);
                    break;
            }
        }
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
}
