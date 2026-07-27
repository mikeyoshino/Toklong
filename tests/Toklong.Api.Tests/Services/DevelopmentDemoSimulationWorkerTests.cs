using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Toklong.Api.Services;
using Toklong.Application;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;

namespace Toklong.Api.Tests.Services;

public sealed class DevelopmentDemoSimulationWorkerTests
{
    [Fact]
    public void Enabled_simulation_is_rejected_outside_development()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DevelopmentDemoSimulation:Enabled"] = "true"
                })
            .Build();

        Assert.Throws<InvalidOperationException>(
            () => DevelopmentDemoSimulationOptions.From(
                configuration,
                new TestHostEnvironment(Environments.Production)));
    }

    [Fact]
    public async Task Each_demo_step_uses_normal_carrier_and_payout_events()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<
            DevelopmentShippingQuoteProvider>();
        services.AddSingleton<IShippingQuoteProvider>(
            provider => provider.GetRequiredService<
                DevelopmentShippingQuoteProvider>());
        services.AddSingleton<IShipmentProvider>(
            provider => provider.GetRequiredService<
                DevelopmentShippingQuoteProvider>());
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ITransactionRepository, TransactionRepository>();
        services.AddScoped<IUnitOfWork>(
            provider => provider.GetRequiredService<ToklongDbContext>());
        var databaseName = Guid.NewGuid().ToString("N");
        services.AddDbContext<ToklongDbContext>(
            options => options.UseInMemoryDatabase(
                databaseName));
        await using var provider = services.BuildServiceProvider();
        var transactionId = await SeedTrackingSubmittedAsync(provider);
        var worker = new DevelopmentDemoSimulationWorker(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new DevelopmentDemoSimulationOptions
            {
                Enabled = true,
                StepIntervalSeconds = 1
            },
            TimeProvider.System,
            provider
                .GetRequiredService<
                    ILogger<DevelopmentDemoSimulationWorker>>());

        Assert.Equal(1, await worker.RunOneStepAsync());
        Assert.Equal(
            TransactionState.InTransit,
            await GetStateAsync(provider, transactionId));

        Assert.Equal(1, await worker.RunOneStepAsync());
        Assert.Equal(
            TransactionState.DeliveredDisputeWindow,
            await GetStateAsync(provider, transactionId));

        await StartManualPayoutAsync(provider, transactionId);
        Assert.Equal(1, await worker.RunOneStepAsync());

        await using var verificationScope =
            provider.CreateAsyncScope();
        var completed = await verificationScope.ServiceProvider
            .GetRequiredService<ITransactionRepository>()
            .GetByIdAsync(transactionId, default);
        Assert.NotNull(completed);
        Assert.Equal(TransactionState.PaidOut, completed.State);
        Assert.Contains(
            completed.ExternalEvents,
            item => item.EventType == "in_transit");
        Assert.Contains(
            completed.ExternalEvents,
            item => item.EventType == "delivered");
        Assert.Contains(
            completed.ExternalEvents,
            item => item.EventType == "payout.confirmed");
    }

    private static async Task<Guid> SeedTrackingSubmittedAsync(
        ServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var transitions = scope.ServiceProvider
            .GetRequiredService<TransactionTransitionService>();
        var now = DateTimeOffset.UtcNow.AddMinutes(-10);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66800000000",
            FulfillmentType.PhysicalShipment,
            "กล้องทดสอบ",
            "กล้องพร้อมเลนส์ มีรอยเล็กน้อย",
            ConditionCode.UsedGood,
            "มีรอยเล็กน้อย",
            "https://example.com/photo.jpg",
            120_000,
            "terms-v1",
            now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            now.AddMinutes(1),
            transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                now.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66800000000",
            "กรุงเทพฯ",
            now.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "demo-payment",
            now.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH123412345",
            now.AddMinutes(4),
            transitions);
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        return transaction.Id;
    }

    private static async Task StartManualPayoutAsync(
        ServiceProvider provider,
        Guid transactionId)
    {
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider
            .GetRequiredService<ITransactionRepository>();
        var database = scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>();
        var transitions = scope.ServiceProvider
            .GetRequiredService<TransactionTransitionService>();
        var transaction = await repository.GetByIdAsync(
            transactionId,
            default);
        Assert.NotNull(transaction);
        var now = DateTimeOffset.UtcNow;
        transaction.ConfirmReceipt(
            transaction.BuyerAccessToken!,
            now,
            transitions);
        transaction.StartPayout(
            "PAYOUT-DEMO",
            now,
            transitions,
            "manual-bank");
        await database.SaveChangesAsync();
    }

    private static async Task<TransactionState> GetStateAsync(
        ServiceProvider provider,
        Guid transactionId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider
            .GetRequiredService<ToklongDbContext>()
            .Transactions
            .AsNoTracking()
            .Where(transaction => transaction.Id == transactionId)
            .Select(transaction => transaction.State)
            .SingleAsync();
    }

    private sealed class TestHostEnvironment(string environmentName)
        : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Toklong.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }
}
