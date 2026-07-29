using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Shipping;

public sealed class ShippingOperationPersistenceTests
{
    static ShippingOperationPersistenceTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 19, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint =
        new('a', 64);

    [Fact]
    public async Task Transaction_shipment_and_operation_commit_together()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var transaction = NewTransaction();
        var (shipment, operation) = QueueOutbound(transaction);
        await using (var context = database.CreateContext())
        {
            context.Transactions.Add(transaction);
            await context.SaveChangesAsync();
        }

        await using var assertionContext = database.CreateContext();
        Assert.NotNull(await assertionContext.Transactions.FindAsync(
            transaction.Id));
        Assert.NotNull(await assertionContext.ManagedShipments.FindAsync(
            shipment.Id));
        Assert.NotNull(await assertionContext.ShippingOperations.FindAsync(
            operation.Id));
    }

    [Fact]
    public void Model_has_required_shipping_unique_and_due_indexes()
    {
        using var context = RelationalDatabase.CreateModelContext();
        var shipment = context.Model.FindEntityType(
            typeof(ManagedShipment))!;
        var operation = context.Model.FindEntityType(
            typeof(ShippingOperation))!;
        var adjustment = context.Model.FindEntityType(
            typeof(ProviderShippingAdjustment))!;
        var insurance = context.Model.FindEntityType(
            typeof(ShippingInsuranceCase))!;

        Assert.Contains(
            shipment.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(ManagedShipment.TransactionId),
                             nameof(ManagedShipment.Direction)
                         ]));
        Assert.Contains(
            operation.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Single().Name ==
                         nameof(ShippingOperation.IdempotencyKey));
        Assert.Contains(
            operation.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(ShippingOperation.Status),
                    nameof(ShippingOperation.NextAttemptAt)
                ]));
        Assert.True(operation.FindProperty(
            nameof(ShippingOperation.Version))!.IsConcurrencyToken);
        Assert.Contains(
            adjustment.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Single().Name ==
                         nameof(ProviderShippingAdjustment.ProviderReference));
        Assert.Contains(
            insurance.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Single().Name ==
                         nameof(ShippingInsuranceCase.ProviderCaseReference));
    }

    [Fact]
    public async Task Live_lease_is_not_reclaimed_and_expired_lease_is()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var transaction = NewTransaction();
        var (_, operation) = QueueOutbound(transaction);
        await using (var setup = database.CreateContext())
        {
            setup.Transactions.Add(transaction);
            await setup.SaveChangesAsync();
        }

        await using (var firstContext = database.CreateContext())
        {
            var claimed = await new ShippingOperationRepository(firstContext)
                .ClaimDueAsync(
                    "worker-a",
                    Now,
                    TimeSpan.FromMinutes(5),
                    default);
            Assert.Equal(operation.Id, claimed?.Id);
        }
        await using (var liveContext = database.CreateContext())
        {
            var live = await new ShippingOperationRepository(liveContext)
                .ClaimDueAsync(
                    "worker-b",
                    Now.AddMinutes(1),
                    TimeSpan.FromMinutes(5),
                    default);
            Assert.Null(live);
        }
        await using (var expiredContext = database.CreateContext())
        {
            var reclaimed =
                await new ShippingOperationRepository(expiredContext)
                    .ClaimDueAsync(
                        "worker-b",
                        Now.AddMinutes(6),
                        TimeSpan.FromMinutes(5),
                        default);
            Assert.Equal(operation.Id, reclaimed?.Id);
            Assert.Equal("worker-b", reclaimed?.LeaseOwner);
            Assert.Equal(2, reclaimed?.AttemptCount);
        }
    }

    [Fact]
    public async Task Concurrent_claims_return_the_operation_to_one_worker()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var transaction = NewTransaction();
        var (_, operation) = QueueOutbound(transaction);
        await using (var setup = database.CreateContext())
        {
            setup.Transactions.Add(transaction);
            await setup.SaveChangesAsync();
        }

        await using var firstContext = database.CreateContext();
        await using var secondContext = database.CreateContext();
        var first = new ShippingOperationRepository(firstContext)
            .ClaimDueAsync(
                "worker-a",
                Now,
                TimeSpan.FromMinutes(5),
                default);
        var second = new ShippingOperationRepository(secondContext)
            .ClaimDueAsync(
                "worker-b",
                Now,
                TimeSpan.FromMinutes(5),
                default);

        var results = await Task.WhenAll(first, second);

        Assert.Single(results, result => result?.Id == operation.Id);
        Assert.Single(results, result => result is null);
    }

    [Fact]
    public async Task Duplicate_operation_idempotency_key_is_rejected()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var first = NewTransaction();
        var second = NewTransaction();
        var (_, firstOperation) = QueueOutbound(first);
        var secondShipment = ManagedShipment.CreateOutbound(
            second.Id,
            ShipmentDraft(),
            Now);
        var duplicate = ShippingOperation.Queue(
            second.Id,
            secondShipment.Id,
            ShippingOperationType.BookOutbound,
            firstOperation.IdempotencyKey,
            Fingerprint,
            Now);
        second.QueueManagedShipment(
            secondShipment,
            duplicate,
            ActorRole.System,
            "shipping-orchestrator",
            Now);

        await using var context = database.CreateContext();
        context.Transactions.Add(first);
        await context.SaveChangesAsync();
        context.Transactions.Add(second);

        await Assert.ThrowsAsync<DbUpdateException>(
            () => context.SaveChangesAsync());
    }

    private static SaleTransaction NewTransaction() =>
        TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "0800000000",
            FulfillmentType.PhysicalShipment,
            "กล้อง",
            "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood,
            "",
            null,
            120_000,
            "terms-v1",
            Now,
            new TransactionTransitionService());

    private static (ManagedShipment Shipment, ShippingOperation Operation)
        QueueOutbound(SaleTransaction transaction)
    {
        var shipment = ManagedShipment.CreateOutbound(
            transaction.Id,
            ShipmentDraft(),
            Now);
        var operation = ShippingOperation.Queue(
            transaction.Id,
            shipment.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:test",
            Fingerprint,
            Now);
        transaction.QueueManagedShipment(
            shipment,
            operation,
            ActorRole.System,
            "shipping-orchestrator",
            Now);
        return (shipment, operation);
    }

    private static ManagedShipmentDraft ShipmentDraft() =>
        new(
            "shippop",
            "seller-origin-snapshot",
            "buyer-destination-snapshot",
            "กล้องพร้อมเลนส์",
            1_200,
            20,
            30,
            15,
            "THAIPOST",
            "EMST",
            "ไปรษณีย์ไทย EMS",
            5_200,
            1_100,
            120_000,
            "FULL_VALUE",
            "quote-reference",
            Now.AddHours(2));

    private sealed class RelationalDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<ToklongDbContext> options;

        private RelationalDatabase(
            SqliteConnection anchor,
            DbContextOptions<ToklongDbContext> options)
        {
            this.anchor = anchor;
            this.options = options;
        }

        public static async Task<RelationalDatabase> CreateAsync()
        {
            var connectionString =
                $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var options =
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseSqlite(connectionString)
                    .Options;
            await using var context = new ToklongDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new RelationalDatabase(anchor, options);
        }

        public static ToklongDbContext CreateModelContext()
        {
            var options =
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseInMemoryDatabase(Guid.NewGuid().ToString())
                    .Options;
            return new ToklongDbContext(options);
        }

        public ToklongDbContext CreateContext() => new(options);

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }
}
