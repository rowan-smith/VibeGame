using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace PoleCore.Logging;

internal static class LoggingUtils
{
    private static readonly string[] EnvironmentVariables = ["ASPNETCORE_ENVIRONMENT", "DOTNET_ENVIRONMENT"];
    private const string DefaultEnvironment = "Production";
    private const string DevelopmentEnvironment = "Development";

    public static IConfiguration LoadConfiguration()
    {
        var environmentName = GetEnvironmentName();
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{environmentName}.json", optional: true, reloadOnChange: true)
            .AddUserSecretsIfDevelopment(environmentName)
            .AddEnvironmentVariables()
            .Build();
    }

    private static string GetEnvironmentName()
    {
        var environment = EnvironmentVariables
            .Select(Environment.GetEnvironmentVariable)
            .FirstOrDefault(env => !string.IsNullOrEmpty(env));

        if (environment is not null)
        {
            return environment;
        }

        // Fallback to default environment
        return DefaultEnvironment;
    }

    private static IConfigurationBuilder AddUserSecretsIfDevelopment(this IConfigurationBuilder builder, string environmentName)
    {
        if (environmentName.Equals(DevelopmentEnvironment, StringComparison.OrdinalIgnoreCase))
        {
            var assembly = Assembly.GetEntryAssembly();
            if (assembly is not null)
            {
                builder.AddUserSecrets(assembly, optional: true);
            }
        }
        return builder;
    }
}
