using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Persistence;

public sealed class BookingAttemptPersistenceTests
{
    static BookingAttemptPersistenceTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 16, 0, 0, TimeSpan.Zero);
    private static readonly Guid BuyerId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly string Fingerprint =
        new('a', 64);

    [Fact]
    public void Model_has_booking_attempt_coordination_indexes()
    {
        using var context = RelationalDatabase.CreateModelContext();
        var entity = context.Model.FindEntityType(
            typeof(BookingAttempt))!;

        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(
                             property => property.Name)
                         .SequenceEqual([
                             nameof(BookingAttempt.TransactionId),
                             nameof(BookingAttempt.IdempotencyKey)
                         ]));
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Single().Name ==
                     nameof(BookingAttempt.ProviderReference));
        Assert.Contains(
            entity.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(
                             property => property.Name)
                         .SequenceEqual([
                             nameof(BookingAttempt.TransactionId),
                             nameof(BookingAttempt.AttemptNumber)
                         ]));
        Assert.True(
            entity.FindProperty(
                nameof(BookingAttempt.Version))!
                .IsConcurrencyToken);
    }

    [Fact]
    public async Task Repeated_key_reuses_the_claimed_attempt()
    {
        await using var database =
            await RelationalDatabase.CreateAsync();
        var (transaction, shipment) =
            await database.SeedShipmentAsync();
        var request = new AcquireBookingAttempt(
            transaction.Id,
            shipment.Id,
            BuyerId,
            "checkout-001",
            Fingerprint,
            Now);

        await using var firstContext =
            database.CreateContext();
        var first = await new BookingAttemptRepository(
                firstContext)
            .AcquireAsync(
                request,
                default);
        await using var secondContext =
            database.CreateContext();
        var second = await new BookingAttemptRepository(
                secondContext)
            .AcquireAsync(
                request,
                default);

        Assert.Equal(
            BookingAttemptAcquireState.Acquired,
            first.State);
        Assert.Equal(
            BookingAttemptAcquireState.InProgress,
            second.State);
        Assert.Equal(
            first.Attempt.Id,
            second.Attempt.Id);
    }

    [Fact]
    public async Task Stale_provider_call_becomes_timed_out_without_replay()
    {
        await using var database =
            await RelationalDatabase.CreateAsync();
        var (transaction, shipment) =
            await database.SeedShipmentAsync();
        var request = new AcquireBookingAttempt(
            transaction.Id,
            shipment.Id,
            BuyerId,
            "checkout-stale",
            Fingerprint,
            Now);
        await using (var firstContext =
                     database.CreateContext())
        {
            var first =
                await new BookingAttemptRepository(
                        firstContext)
                    .AcquireAsync(
                        request,
                        default);
            Assert.Equal(
                BookingAttemptAcquireState.Acquired,
                first.State);
        }

        await using var retryContext =
            database.CreateContext();
        var stale = await new BookingAttemptRepository(
                retryContext)
            .AcquireAsync(
                request with
                {
                    Now = Now.AddSeconds(4)
                },
                default);

        Assert.Equal(
            BookingAttemptAcquireState.TimedOut,
            stale.State);
        Assert.Equal(
            "checkout-process-interrupted",
            stale.Attempt.SafeFailureCode);
    }

    [Fact]
    public async Task Fourth_provider_attempt_is_rejected()
    {
        await using var database =
            await RelationalDatabase.CreateAsync();
        var (transaction, shipment) =
            await database.SeedShipmentAsync();

        for (var number = 1; number <= 3; number++)
        {
            await using var context =
                database.CreateContext();
            var repository =
                new BookingAttemptRepository(context);
            var result = await repository.AcquireAsync(
                new AcquireBookingAttempt(
                    transaction.Id,
                    shipment.Id,
                    BuyerId,
                    $"checkout-{number}",
                    Fingerprint,
                    Now.AddSeconds(number * 4)),
                default);
            Assert.Equal(
                BookingAttemptAcquireState.Acquired,
                result.State);
            result.Attempt.TimeOut(
                "shippop-timeout",
                Now.AddSeconds(number * 4 + 1));
            await context.SaveChangesAsync();
        }

        await using var fourthContext =
            database.CreateContext();
        var fourth =
            await new BookingAttemptRepository(
                    fourthContext)
                .AcquireAsync(
                    new AcquireBookingAttempt(
                        transaction.Id,
                        shipment.Id,
                        BuyerId,
                        "checkout-4",
                        Fingerprint,
                        Now.AddSeconds(20)),
                    default);

        Assert.Equal(
            BookingAttemptAcquireState.RetryLimitReached,
            fourth.State);
        Assert.Equal(3, fourth.Attempt.AttemptNumber);
    }

    private sealed class RelationalDatabase
        : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<ToklongDbContext>
            options;

        private RelationalDatabase(
            SqliteConnection anchor,
            DbContextOptions<ToklongDbContext> options)
        {
            this.anchor = anchor;
            this.options = options;
        }

        public static async Task<RelationalDatabase>
            CreateAsync()
        {
            var connectionString =
                $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var anchor =
                new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var options =
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseSqlite(connectionString)
                    .Options;
            await using var context =
                new ToklongDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new RelationalDatabase(
                anchor,
                options);
        }

        public static ToklongDbContext
            CreateModelContext() =>
            new(
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseInMemoryDatabase(
                        Guid.NewGuid().ToString())
                    .Options);

        public ToklongDbContext CreateContext() =>
            new(options);

        public async Task<(
            SaleTransaction Transaction,
            ManagedShipment Shipment)> SeedShipmentAsync()
        {
            var transaction =
                TestTransactionFactory.CreateBuyerOffer(
                    BuyerId,
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
            var shipment =
                ManagedShipment.CreateOutbound(
                    transaction.Id,
                    new ManagedShipmentDraft(
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
                        600,
                        120_000,
                        "FULL_VALUE",
                        "quote-reference",
                        Now.AddHours(2)),
                    Now);
            await using var context =
                CreateContext();
            context.Transactions.Add(transaction);
            context.ManagedShipments.Add(shipment);
            await context.SaveChangesAsync();
            return (transaction, shipment);
        }

        public ValueTask DisposeAsync() =>
            anchor.DisposeAsync();
    }
}
