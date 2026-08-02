using Toklong.Application;
using Toklong.Infrastructure;
using Toklong.Worker;
using Toklong.Infrastructure.Services;
using Toklong.Application.Features.Shipping.ProcessShippingOperations;

var builder = Host.CreateApplicationBuilder(args);

ProductionConfigurationValidator.Validate(
    builder.Configuration,
    builder.Environment,
    requireMobileLinks: false,
    requirePersistentStorage: false);
DisputeEvidenceStoreOptions.ValidateConfiguration(
    builder.Configuration,
    builder.Environment);
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddToklongDataProtection(
    builder.Configuration,
    builder.Environment);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ShippingWorkerOptions>(
    builder.Configuration.GetSection(
        ShippingWorkerOptions.SectionName));
builder.Services.AddHostedService<DeadlineWorker>();
builder.Services.AddHostedService<FinancialOperationsWorker>();
builder.Services.AddHostedService<ShippingOperationsWorker>();
builder.Services.AddHostedService<RetentionWorker>();
if (builder.Configuration.GetValue<bool>("Notifications:Enabled"))
    builder.Services.AddHostedService<NotificationOutboxWorker>();

await builder.Build().RunAsync();
