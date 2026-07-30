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
        Assert.DoesNotContain(fixture.Transaction.AuditEvents, audit =>
            audit.Name is "parcel_protection.offered" or
                "parcel_protection.unavailable");
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
    }
}
