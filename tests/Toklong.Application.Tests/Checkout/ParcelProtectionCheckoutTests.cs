using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Checkout.ChooseParcelProtection;
using Toklong.Application.Features.Checkout.GetParcelProtection;
using Toklong.Application.Features.Checkout.PrepareParcelProtection;
using Toklong.Application.Pricing;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Checkout;

public sealed class ParcelProtectionCheckoutTests
{
    static ParcelProtectionCheckoutTests() =>
        SQLitePCL.raw.SetProvider(
            new SQLitePCL.SQLite3Provider_sqlite3());

    private static readonly DateTimeOffset Now =
        new(2026, 7, 30, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Within_included_limit_queues_unprotected_booking_without_prompt()
    {
        await using var fixture = await Fixture.CreateAsync(100_000);

        var view = await fixture.Prepare.Handle(
            new PrepareParcelProtectionCommand(
                fixture.Transaction.Id, fixture.BuyerId,
                "prepare-included-coverage"), default);

        Assert.False(view.RequiresChoice);
        Assert.Contains(fixture.Transaction.AuditEvents, audit =>
            audit.Name == "parcel_protection.offered");
        var result = await fixture.ChooseHandler.Handle(fixture.Choose(false), default);

        Assert.Equal("preparing_shipping", result.BookingStatus);
        Assert.Single(fixture.Transaction.ManagedShipments);
        Assert.Single(fixture.Transaction.ShippingOperations);
        Assert.Equal(0, fixture.Transaction.ManagedShipments.Single().InsuranceFeeSatang);
        Assert.Equal(0, fixture.Transaction.ManagedShipments.Single().DeclaredValueSatang);
        Assert.Equal(ParcelProtectionElectionStatus.Declined,
            fixture.Transaction.ParcelProtectionElection);
    }

    [Fact]
    public async Task Accepted_add_on_revalidates_price_and_queues_exact_booking()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        var prepared = await fixture.Prepare.Handle(fixture.PrepareCommand(), default);

        var result = await fixture.ChooseHandler.Handle(
            fixture.Choose(true, prepared.OptionReference, 6_000), default);

        var shipment = fixture.Transaction.ManagedShipments.Single();
        Assert.Equal(4_500, shipment.InsuranceFeeSatang);
        Assert.Equal(450_000, shipment.DeclaredValueSatang);
        Assert.Equal("DEV_FULL_VALUE", shipment.InsuranceCode);
        Assert.Equal(6_000, fixture.Transaction.ParcelInsuranceFeeSatang);
        Assert.Equal("preparing_shipping", result.BookingStatus);
    }

    [Fact]
    public async Task Changed_price_does_not_queue_booking()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        var prepared = await fixture.Prepare.Handle(fixture.PrepareCommand(), default);

        await Assert.ThrowsAsync<ParcelProtectionOptionChangedException>(() => fixture.ChooseHandler.Handle(
            fixture.Choose(true, prepared.OptionReference, 5_900), default));

        Assert.Empty(fixture.Transaction.ManagedShipments);
        Assert.Empty(fixture.Transaction.ShippingOperations);
    }

    [Fact]
    public async Task Prepare_records_one_offer_event_for_same_idempotency_key()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        var request = fixture.PrepareCommand("prepare-once-for-resume");

        await fixture.Prepare.Handle(request, default);
        await fixture.Prepare.Handle(request, default);

        Assert.Single(fixture.Transaction.AuditEvents, a =>
            a.IdempotencyKey == "prepare-once-for-resume" &&
            a.Name == "parcel_protection.offered");
        Assert.Empty(fixture.Transaction.ManagedShipments);
    }

    [Theory]
    [InlineData(100_000, false)]
    [InlineData(450_000, true)]
    public async Task Prepared_protection_resumes_exact_buyer_safe_view_after_fresh_relational_reload(
        long priceSatang, bool requiresChoice)
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var clock = new MutableClock(Now.AddMinutes(1));
        Guid transactionId;
        Guid buyerId;
        BuyerParcelProtectionView prepared;

        await using (var write = database.CreateContext())
        {
            var transaction = CreateAcceptedTransaction(priceSatang);
            transactionId = transaction.Id;
            buyerId = transaction.BuyerId!.Value;
            write.Transactions.Add(transaction);
            await write.SaveChangesAsync();
            prepared = await new PrepareParcelProtectionHandler(
                new TransactionRepository(write), new TestProtectionProvider(),
                new ParcelProtectionPricingPolicy(), write, clock).Handle(
                new PrepareParcelProtectionCommand(transactionId, buyerId,
                    $"prepare-relational-{priceSatang}"), default);
        }

        await using var read = database.CreateContext();
        var resumed = await new GetParcelProtectionHandler(
            new TransactionRepository(read), clock).Handle(
            new GetParcelProtectionQuery(transactionId, buyerId), default);
        var json = JsonSerializer.Serialize(resumed);

        Assert.Equal(prepared, resumed);
        Assert.Equal(requiresChoice, resumed.RequiresChoice);
        Assert.Equal(100_000, resumed.IncludedCoverageLimitSatang);
        Assert.Equal(450_000, resumed.MaximumCoverageLimitSatang);
        Assert.Equal(6_000, resumed.CustomerPriceSatang);
        Assert.DoesNotContain("providerCost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serviceFee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("development-shipping", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unavailable_preparation_resumes_exact_buyer_safe_view_after_fresh_relational_reload()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var clock = new MutableClock(Now.AddMinutes(1));
        var provider = new TestProtectionProvider
        {
            Availability = new ParcelProtectionAvailability(
                100_000, null, ProviderCapabilityCertified: false)
        };
        Guid transactionId;
        Guid buyerId;
        BuyerParcelProtectionView prepared;

        await using (var write = database.CreateContext())
        {
            var transaction = CreateAcceptedTransaction(450_000);
            transactionId = transaction.Id;
            buyerId = transaction.BuyerId!.Value;
            write.Transactions.Add(transaction);
            await write.SaveChangesAsync();
            prepared = await new PrepareParcelProtectionHandler(
                new TransactionRepository(write), provider,
                new ParcelProtectionPricingPolicy(), write, clock).Handle(
                new PrepareParcelProtectionCommand(transactionId, buyerId,
                    "prepare-relational-unavailable"), default);
        }

        await using var read = database.CreateContext();
        var resumed = await new GetParcelProtectionHandler(
            new TransactionRepository(read), clock).Handle(
            new GetParcelProtectionQuery(transactionId, buyerId), default);
        var json = JsonSerializer.Serialize(resumed);

        Assert.Equal(prepared, resumed);
        Assert.Equal("Unavailable", resumed.Election);
        Assert.False(resumed.RequiresChoice);
        Assert.False(resumed.AddOnAvailable);
        Assert.Equal(100_000, resumed.IncludedCoverageLimitSatang);
        Assert.Null(resumed.MaximumCoverageLimitSatang);
        Assert.Null(resumed.CustomerPriceSatang);
        Assert.DoesNotContain("providerCost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serviceFee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("development-shipping", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Included_only_preparation_resumes_exact_buyer_safe_view_after_fresh_relational_reload()
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var clock = new MutableClock(Now.AddMinutes(1));
        var provider = new TestProtectionProvider
        {
            Availability = new ParcelProtectionAvailability(
                100_000, null, ProviderCapabilityCertified: true)
        };
        Guid transactionId;
        Guid buyerId;
        BuyerParcelProtectionView prepared;

        await using (var write = database.CreateContext())
        {
            var transaction = CreateAcceptedTransaction(100_000);
            transactionId = transaction.Id;
            buyerId = transaction.BuyerId!.Value;
            write.Transactions.Add(transaction);
            await write.SaveChangesAsync();
            prepared = await new PrepareParcelProtectionHandler(
                new TransactionRepository(write), provider,
                new ParcelProtectionPricingPolicy(), write, clock).Handle(
                new PrepareParcelProtectionCommand(transactionId, buyerId,
                    "prepare-relational-included-only"), default);
        }

        await using var read = database.CreateContext();
        var resumed = await new GetParcelProtectionHandler(
            new TransactionRepository(read), clock).Handle(
            new GetParcelProtectionQuery(transactionId, buyerId), default);
        var json = JsonSerializer.Serialize(resumed);

        Assert.Equal(prepared, resumed);
        Assert.Equal("Pending", resumed.Election);
        Assert.False(resumed.RequiresChoice);
        Assert.False(resumed.AddOnAvailable);
        Assert.Equal(100_000, resumed.IncludedCoverageLimitSatang);
        Assert.Null(resumed.MaximumCoverageLimitSatang);
        Assert.Null(resumed.CustomerPriceSatang);
        Assert.DoesNotContain("providerCost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("serviceFee", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("development-shipping", json,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Legacy_minimal_preparation_metadata_is_ignored_for_safe_aggregate_fallback()
    {
        var resumed = await ReloadWithPreparedMetadataAsync(
            "{\"AddOnAvailable\":true}");

        AssertSafeLegacyFallback(resumed);
    }

    [Fact]
    public async Task Malformed_current_preparation_metadata_is_ignored_for_safe_aggregate_fallback()
    {
        var resumed = await ReloadWithPreparedMetadataAsync(
            "{\"MetadataVersion\":1,\"RequiresChoice\":false," +
            "\"AddOnAvailable\":false,\"IncludedCoverageLimitSatang\":0," +
            "\"MaximumCoverageLimitSatang\":null,\"CustomerPriceSatang\":null," +
            "\"OptionReference\":null,\"TermsVersion\":null," +
            "\"ExpiresAt\":null,\"Election\":0}");

        AssertSafeLegacyFallback(resumed);
    }

    [Theory]
    [InlineData("buyer@example.com")]
    [InlineData("prepare-ผู้ซื้อ-12345")]
    public async Task Prepare_rejects_unsafe_idempotency_key_before_provider_or_audit_mutation(
        string idempotencyKey)
    {
        await using var fixture = await Fixture.CreateAsync(450_000);

        await Assert.ThrowsAsync<DomainException>(() => fixture.Prepare.Handle(
            fixture.PrepareCommand(idempotencyKey), default));

        Assert.Equal(0, fixture.Provider.AvailabilityCalls);
        Assert.DoesNotContain(fixture.Transaction.AuditEvents, audit =>
            audit.Name is "parcel_protection.offered" or
                "parcel_protection.unavailable");
    }

    [Fact]
    public async Task Unavailable_add_on_is_explicit_and_does_not_call_provider_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        fixture.Provider.Availability = new ParcelProtectionAvailability(
            100_000, null, ProviderCapabilityCertified: false);

        var view = await fixture.Prepare.Handle(fixture.PrepareCommand(), default);
        var result = await fixture.ChooseHandler.Handle(fixture.Choose(false), default);

        Assert.False(view.AddOnAvailable);
        Assert.Equal("Unavailable", view.Election);
        Assert.Equal(ParcelProtectionElectionStatus.Unavailable,
            fixture.Transaction.ParcelProtectionElection);
        Assert.Equal("preparing_shipping", result.BookingStatus);
        Assert.Equal(0, fixture.Provider.ValidateCalls);
    }

    [Fact]
    public async Task Digital_transaction_is_not_applicable_and_creates_no_shipment_operation()
    {
        await using var fixture = await Fixture.CreateAsync(
            450_000, FulfillmentType.DigitalHandoff);

        var view = await fixture.Prepare.Handle(fixture.PrepareCommand(), default);
        var result = await fixture.ChooseHandler.Handle(fixture.Choose(false), default);

        Assert.Equal("NotApplicable", view.Election);
        Assert.Equal("not_applicable", result.BookingStatus);
        Assert.Empty(fixture.Transaction.ManagedShipments);
        Assert.Empty(fixture.Transaction.ShippingOperations);
        Assert.Equal(0, fixture.Provider.AvailabilityCalls);
    }

    [Fact]
    public async Task Buyer_authorization_and_expired_deadline_are_rejected()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);

        await Assert.ThrowsAsync<ForbiddenException>(() => fixture.Prepare.Handle(
            new PrepareParcelProtectionCommand(
                fixture.Transaction.Id, Guid.NewGuid(), "prepare-wrong-buyer"), default));

        fixture.Clock.Now = fixture.Transaction.BuyerPaymentDeadlineAt!.Value;
        await Assert.ThrowsAsync<DomainException>(() => fixture.ChooseHandler.Handle(
            fixture.Choose(false), default));
        Assert.Empty(fixture.Transaction.ManagedShipments);
    }

    [Fact]
    public async Task Expired_quote_and_quote_or_service_mismatch_do_not_queue_booking()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        var prepared = await fixture.Prepare.Handle(fixture.PrepareCommand(), default);
        fixture.Provider.Option = fixture.Provider.Option with
        {
            ExpiresAt = Now,
            Provider = "other-provider"
        };

        await Assert.ThrowsAsync<ParcelProtectionOptionChangedException>(() => fixture.ChooseHandler.Handle(
            fixture.Choose(true, prepared.OptionReference, 6_000), default));
        Assert.Empty(fixture.Transaction.ManagedShipments);
    }

    [Fact]
    public async Task Changed_terms_or_coverage_option_does_not_queue_booking()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        var prepared = await fixture.Prepare.Handle(fixture.PrepareCommand(), default);
        fixture.Provider.Option = fixture.Provider.Option with
        {
            TermsVersion = "parcel-protection-v2",
            SelectedCoverageLimitSatang = 449_999
        };

        await Assert.ThrowsAsync<ParcelProtectionOptionChangedException>(() => fixture.ChooseHandler.Handle(
            fixture.Choose(true, prepared.OptionReference, 6_000), default));

        Assert.Empty(fixture.Transaction.ManagedShipments);
        Assert.Empty(fixture.Transaction.ShippingOperations);
    }

    [Fact]
    public async Task Invalid_idempotency_key_is_rejected_before_a_provider_or_booking_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);

        await Assert.ThrowsAsync<DomainException>(() => fixture.ChooseHandler.Handle(
            fixture.Choose(false, key: "invalid key"), default));

        Assert.Equal(0, fixture.Provider.AvailabilityCalls);
        Assert.Empty(fixture.Transaction.ManagedShipments);
    }

    [Fact]
    public async Task Duplicate_idempotency_returns_existing_booking_but_different_second_choice_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        var prepared = await fixture.Prepare.Handle(fixture.PrepareCommand(), default);
        var first = fixture.Choose(true, prepared.OptionReference, 6_000,
            "choose-protection-once");

        await fixture.ChooseHandler.Handle(first, default);
        var duplicate = await fixture.ChooseHandler.Handle(first, default);
        Assert.Equal("preparing_shipping", duplicate.BookingStatus);
        Assert.Single(fixture.Transaction.ManagedShipments);

        await Assert.ThrowsAsync<DomainException>(() => fixture.ChooseHandler.Handle(
            fixture.Choose(false, null, null, "choose-protection-once"), default));
    }

    [Fact]
    public async Task Reserved_change_same_key_replays_without_provider_or_shipping_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        await fixture.ChooseHandler.Handle(
            fixture.Choose(true, "protected-option", 6_000, "choose-original-0001"),
            default);
        ReserveCurrentShipment(fixture.Transaction);
        var request = fixture.Choose(false, null, null, "change-reserved-0001");

        var first = await fixture.ChooseHandler.Handle(request, default);
        fixture.Provider.ResetCalls();
        var replay = await fixture.ChooseHandler.Handle(request, default);

        Assert.Equal("cancelling_shipping", first.BookingStatus);
        Assert.Equal(first.BookingStatus, replay.BookingStatus);
        Assert.Equal(0, fixture.Provider.AvailabilityCalls);
        Assert.Equal(0, fixture.Provider.ValidateCalls);
        Assert.Single(fixture.Transaction.ParcelProtectionChangeRequests);
        Assert.Single(fixture.Transaction.ShippingOperations,
            operation => operation.OperationType == ShippingOperationType.CancelOutbound);
    }

    [Fact]
    public async Task Reserved_change_same_key_conflict_rejects_without_provider_or_shipping_mutation()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        await fixture.ChooseHandler.Handle(
            fixture.Choose(true, "protected-option", 6_000, "choose-original-0001"),
            default);
        ReserveCurrentShipment(fixture.Transaction);
        await fixture.ChooseHandler.Handle(
            fixture.Choose(false, null, null, "change-reserved-0001"), default);
        fixture.Provider.ResetCalls();

        await Assert.ThrowsAsync<DomainException>(() => fixture.ChooseHandler.Handle(
            fixture.Choose(true, "protected-option", 6_000,
                "change-reserved-0001"), default));

        Assert.Equal(0, fixture.Provider.AvailabilityCalls);
        Assert.Equal(0, fixture.Provider.ValidateCalls);
        Assert.Single(fixture.Transaction.ParcelProtectionChangeRequests);
        Assert.Single(fixture.Transaction.ShippingOperations,
            operation => operation.OperationType == ShippingOperationType.CancelOutbound);
    }

    [Fact]
    public async Task Buyer_checkout_annex_evidence_is_append_only()
    {
        await using var fixture = await Fixture.CreateAsync(450_000);
        await fixture.ChooseHandler.Handle(fixture.Choose(false), default);
        fixture.Transaction.BeginCheckout(
            "ผู้ซื้อทดสอบ", "0800000000", Now.AddMinutes(2),
            new TransactionTransitionService(), "manual-bank", null,
            0, 0, fixture.Transaction.PriceSatang, "fee-v1");
        fixture.Database.ChangeTracker.DetectChanges();
        Assert.Equal(EntityState.Added,
            fixture.Database.Entry(Assert.Single(
                fixture.Transaction.BuyerCheckoutAnnexAcceptances)).State);
        await fixture.Database.SaveChangesAsync();
        var annex = Assert.Single(
            fixture.Transaction.BuyerCheckoutAnnexAcceptances);

        fixture.Database.Entry(annex)
            .Property(x => x.CanonicalPayloadJson).CurrentValue = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Database.SaveChangesAsync());

        fixture.Database.Entry(annex).State = EntityState.Unchanged;
        fixture.Database.Remove(annex);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            fixture.Database.SaveChangesAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private Fixture(ToklongDbContext database, SaleTransaction transaction,
            Guid buyerId, MutableClock clock, TestProtectionProvider provider)
        {
            Database = database;
            Transaction = transaction;
            BuyerId = buyerId;
            Clock = clock;
            Provider = provider;
            Prepare = new PrepareParcelProtectionHandler(
                new TransactionRepository(database), provider,
                new ParcelProtectionPricingPolicy(), database, clock);
            ChooseHandler = new ChooseParcelProtectionHandler(
                new TransactionRepository(database), provider,
                new ParcelProtectionPricingPolicy(), database, clock);
        }

        public ToklongDbContext Database { get; }
        public SaleTransaction Transaction { get; }
        public Guid BuyerId { get; }
        public MutableClock Clock { get; }
        public TestProtectionProvider Provider { get; }
        public PrepareParcelProtectionHandler Prepare { get; }
        public ChooseParcelProtectionHandler ChooseHandler { get; }

        public static async Task<Fixture> CreateAsync(long price,
            FulfillmentType fulfillment = FulfillmentType.PhysicalShipment)
        {
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var database = new ToklongDbContext(options);
            var transitions = new TransactionTransitionService();
            var buyerId = Guid.NewGuid();
            var transaction = TestTransactionFactory.CreateBuyerOffer(
                buyerId, "ผู้ซื้อทดสอบ", "0800000000", fulfillment,
                "กล้อง", "กล้องพร้อมเลนส์", ConditionCode.UsedGood,
                "ไม่มี", null, price, "terms-v1", Now, transitions);
            var quote = new AcceptedShippingQuote(
                TestTransactionFactory.ShippingOriginAddress,
                TestTransactionFactory.DeliveryProvinceName,
                TestTransactionFactory.DeliveryPostalCode,
                1_200, 20, 30, 15, "development-shipping", "quote-001",
                "THAIPOST", "EMST", "EMS", 5_000, 0, 0, null,
                Now.AddHours(2), TestTransactionFactory.DeliveryDistrictName,
                TestTransactionFactory.DeliverySubdistrictName,
                OriginAddressLine: TestTransactionFactory.ShippingOriginAddress);
            transaction.AcceptBuyerOffer(Guid.NewGuid(), "ผู้ขายทดสอบ",
                "0811111111", "KBANK", "ผู้ขายทดสอบ", "1234567890", true,
                Now, transitions, 0, 0, price, "fee-v1",
                fulfillment == FulfillmentType.PhysicalShipment ? quote : null);
            database.Transactions.Add(transaction);
            await database.SaveChangesAsync();
            return new Fixture(database, transaction, buyerId,
                new MutableClock(Now.AddMinutes(1)), new TestProtectionProvider());
        }

        public PrepareParcelProtectionCommand PrepareCommand(
            string key = "prepare-protection-choice") =>
            new(Transaction.Id, BuyerId, key);

        public ChooseParcelProtectionCommand Choose(bool addProtection,
            string? optionReference = null, long? price = null,
            string key = "choose-parcel-protection") =>
            new(Transaction.Id, BuyerId, addProtection, optionReference, price, key);

        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }

    private static SaleTransaction CreateAcceptedTransaction(long priceSatang)
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(), "ผู้ซื้อทดสอบ", "0800000000",
            FulfillmentType.PhysicalShipment, "กล้อง", "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood, "ไม่มี", null, priceSatang, "terms-v1", Now,
            new TransactionTransitionService());
        var quote = new AcceptedShippingQuote(
            TestTransactionFactory.ShippingOriginAddress,
            TestTransactionFactory.DeliveryProvinceName,
            TestTransactionFactory.DeliveryPostalCode, 1_200, 20, 30, 15,
            "development-shipping", "quote-001", "THAIPOST", "EMST", "EMS",
            5_000, 0, 0, null, Now.AddHours(2),
            TestTransactionFactory.DeliveryDistrictName,
            TestTransactionFactory.DeliverySubdistrictName,
            OriginAddressLine: TestTransactionFactory.ShippingOriginAddress);
        transaction.AcceptBuyerOffer(Guid.NewGuid(), "ผู้ขายทดสอบ", "0811111111",
            "KBANK", "ผู้ขายทดสอบ", "1234567890", true, Now,
            new TransactionTransitionService(), 0, 0, priceSatang, "fee-v1", quote);
        return transaction;
    }

    private static async Task<BuyerParcelProtectionView> ReloadWithPreparedMetadataAsync(
        string metadataJson)
    {
        await using var database = await RelationalDatabase.CreateAsync();
        var clock = new MutableClock(Now.AddMinutes(1));
        Guid transactionId;
        Guid buyerId;
        await using (var write = database.CreateContext())
        {
            var transaction = CreateAcceptedTransaction(450_000);
            transactionId = transaction.Id;
            buyerId = transaction.BuyerId!.Value;
            transaction.RecordParcelProtectionAvailabilityPresented(buyerId,
                new ParcelProtectionPreparedOffer(false, false, 100_000, null,
                    null, null, "parcel-protection-included-v1", null),
                "prepare-metadata-fallback", clock.UtcNow);
            write.Transactions.Add(transaction);
            write.Entry(Assert.Single(transaction.AuditEvents,
                    audit => audit.IdempotencyKey == "prepare-metadata-fallback"))
                .Property(audit => audit.MetadataJson).CurrentValue = metadataJson;
            await write.SaveChangesAsync();
        }

        await using var read = database.CreateContext();
        return await new GetParcelProtectionHandler(
            new TransactionRepository(read), clock).Handle(
            new GetParcelProtectionQuery(transactionId, buyerId), default);
    }

    private static void AssertSafeLegacyFallback(BuyerParcelProtectionView view)
    {
        Assert.Equal("Pending", view.Election);
        Assert.False(view.RequiresChoice);
        Assert.False(view.AddOnAvailable);
        Assert.Equal(0, view.IncludedCoverageLimitSatang);
        Assert.Equal("parcel-protection-included-v1", view.TermsVersion);
        Assert.Null(view.MaximumCoverageLimitSatang);
        Assert.Null(view.CustomerPriceSatang);
        Assert.Null(view.OptionReference);
        Assert.Null(view.ExpiresAt);
    }

    private static void ReserveCurrentShipment(SaleTransaction transaction)
    {
        var shipment = transaction.CurrentOutboundShipment!;
        var booking = Assert.Single(transaction.ShippingOperations);
        booking.Claim("worker-a", Now.AddMinutes(2), TimeSpan.FromMinutes(5));
        shipment.RecordReservation("purchase-001", "provider-001", null,
            Now.AddMinutes(2));
        booking.Succeed("worker-a", "purchase-001", "provider-001",
            Now.AddMinutes(2));
    }

    private sealed class RelationalDatabase : IAsyncDisposable
    {
        private readonly SqliteConnection anchor;
        private readonly DbContextOptions<ToklongDbContext> options;

        private RelationalDatabase(SqliteConnection anchor,
            DbContextOptions<ToklongDbContext> options)
        {
            this.anchor = anchor;
            this.options = options;
        }

        public static async Task<RelationalDatabase> CreateAsync()
        {
            var connectionString = $"Data Source={Guid.NewGuid():N};Mode=Memory;Cache=Shared";
            var anchor = new SqliteConnection(connectionString);
            await anchor.OpenAsync();
            var options = new DbContextOptionsBuilder<ToklongDbContext>()
                .UseSqlite(connectionString).Options;
            await using var context = new ToklongDbContext(options);
            await context.Database.EnsureCreatedAsync();
            return new RelationalDatabase(anchor, options);
        }

        public ToklongDbContext CreateContext() => new(options);

        public ValueTask DisposeAsync() => anchor.DisposeAsync();
    }

    private sealed class MutableClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset Now { get; set; } = now;
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class TestProtectionProvider : IParcelProtectionQuoteProvider
    {
        public int AvailabilityCalls { get; private set; }
        public int ValidateCalls { get; private set; }
        public ParcelProtectionAvailability Availability { get; set; } = new(
            100_000,
            new ProviderParcelProtectionOption(
                "development-shipping", "protected-option", 100_000, 450_000,
                4_500, "parcel-protection-v1", "DEV_FULL_VALUE", Now,
                Now.AddHours(1)), true);
        public ProviderParcelProtectionOption Option { get; set; } = new(
            "development-shipping", "protected-option", 100_000, 450_000,
            4_500, "parcel-protection-v1", "DEV_FULL_VALUE", Now,
            Now.AddHours(1));

        public Task<ParcelProtectionAvailability> GetAvailabilityAsync(
            ParcelProtectionQuoteRequest request, CancellationToken cancellationToken)
        {
            AvailabilityCalls++;
            return Task.FromResult(Availability);
        }

        public Task<ProviderParcelProtectionOption> ValidateOptionAsync(
            ParcelProtectionQuoteRequest request, string optionReference,
            CancellationToken cancellationToken)
        {
            ValidateCalls++;
            if (!string.Equals(optionReference, Option.OptionReference,
                    StringComparison.Ordinal))
                throw new DomainException("ตัวเลือกความคุ้มครองไม่ถูกต้อง");
            return Task.FromResult(Option);
        }

        public void ResetCalls()
        {
            AvailabilityCalls = 0;
            ValidateCalls = 0;
        }
    }
}
