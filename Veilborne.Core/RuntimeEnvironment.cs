using System.Diagnostics;

namespace Veilborne
{
    public static class RuntimeEnvironment
    {
        private static bool? _isDevelopment;

        public static bool IsDevelopmentEnvironment
        {
            get
            {
                if (_isDevelopment.HasValue)
                    return _isDevelopment.Value;

                string? env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                    ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
                _isDevelopment = (!string.IsNullOrWhiteSpace(env) &&
                                  env.Equals("Development", StringComparison.OrdinalIgnoreCase))
                                 || Debugger.IsAttached;
                return _isDevelopment.Value;
            }
        }
    }
}
