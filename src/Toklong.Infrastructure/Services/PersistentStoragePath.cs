using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Toklong.Infrastructure.Services;

public static class PersistentStoragePath
{
    public static string Resolve(
        IHostEnvironment environment,
        IConfiguration configuration,
        string configurationKey,
        string relativeFallback)
    {
        var configuredPath = configuration[configurationKey]?.Trim();
        var path = string.IsNullOrWhiteSpace(configuredPath)
            ? relativeFallback
            : configuredPath;

        return Path.GetFullPath(
            Path.IsPathFullyQualified(path)
                ? path
                : Path.Combine(environment.ContentRootPath, path));
    }
}
