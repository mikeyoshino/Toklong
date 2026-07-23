using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Security;
using Toklong.Infrastructure.Services;

namespace Toklong.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ToklongDbContext>(options =>
            options.UseNpgsql(
                configuration.GetConnectionString("ToklongDatabase"),
                npgsql => npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<ISellerRepository, SellerRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<ToklongDbContext>());
        services.AddHttpClient<IListingImportService, ListingImportService>(client =>
            {
                client.Timeout = TimeSpan.FromSeconds(12);
                client.DefaultRequestHeaders.UserAgent.ParseAdd(
                    "ToklongListingImporter/1.0 (+https://toklong.local)");
            })
            .ConfigurePrimaryHttpMessageHandler(ListingImportService.CreateSafeHandler);
        services.AddSingleton(new ListingAiOptions
        {
            ApiKey = configuration[$"{ListingAiOptions.SectionName}:ApiKey"] ?? "",
            Model = configuration[$"{ListingAiOptions.SectionName}:Model"] ?? "gpt-5.6-luna"
        });
        services.AddSingleton<IListingImageAnalysisService, OpenAiListingImageAnalysisService>();
        services.AddSingleton<IImportedProductImageStore, ImportedProductImageStore>();
        services.AddSingleton<IOtpVerificationProvider, DevelopmentOtpVerificationProvider>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IManualPayoutProvider, ManualPayoutProvider>();
        services.AddSingleton(new ReconciliationOptions
        {
            SigningSecret = configuration[$"{ReconciliationOptions.SectionName}:SigningSecret"] ?? ""
        });
        services.AddSingleton<IWebhookSignatureVerifier, HmacWebhookSignatureVerifier>();
        return services;
    }
}
