using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Toklong.Infrastructure.Services;

public static class ToklongDataProtectionExtensions
{
    private const string ApplicationName = "Toklong.MobileApi";

    public static IServiceCollection AddToklongDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var keysPath = PersistentStoragePath.Resolve(
            environment,
            configuration,
            "DataProtection:KeysPath",
            "App_Data/data-protection-keys");
        Directory.CreateDirectory(keysPath);

        var dataProtection = services.AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath));
        var certificate =
            DataProtectionCertificateLoader.Load(configuration);
        if (certificate is not null)
            dataProtection.ProtectKeysWithCertificate(certificate);

        return services;
    }
}
