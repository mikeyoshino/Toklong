using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Toklong.Application;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Security;

public sealed class ToklongDataProtectionRegistrationTests
{
    [Fact]
    public void Non_web_host_can_build_all_application_services()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"toklong-data-protection-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["DataProtection:KeysPath"] =
                            Path.Combine(root, "keys"),
                        ["ShippingQuotes:Provider"] =
                            "Development"
                    })
                .Build();
            var environment = new TestEnvironment(root);
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);
            services.AddSingleton<IHostEnvironment>(environment);
            services.AddSingleton(TimeProvider.System);
            services.AddApplication();
            services.AddInfrastructure(configuration);
            services.AddToklongDataProtection(
                configuration,
                environment);

            using var provider = services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });

            Assert.NotNull(
                provider.GetRequiredService<
                    IDataProtectionProvider>());
            Assert.NotNull(
                provider.GetRequiredService<
                    IAccountNameAuditEvidenceWriter>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class TestEnvironment(string contentRoot)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } =
            Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
