using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Checkout.PreparePaymentSheet;
using Toklong.Application.Features.Shipping;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Pricing;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Payments;

public sealed class PaymentDeadlineTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 25, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Pending_physical_booking_is_rejected_before_payment_provider_call()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var repository = new TransactionRepository(db);
        var buyers = new BuyerRepository(db);
        var transitions = new TransactionTransitionService();
        var buyer = BuyerAccount.Create(
            "+66811111113",
            "ผู้ซื้อ รอจองขนส่ง",
            "buyer-booking@example.com",
            Start);
        await buyers.AddAsync(buyer, default);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            buyer.Id,
            buyer.FullName,
            buyer.PhoneNumber,
            FulfillmentType.PhysicalShipment,
            "กล้อง",
            "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            450_000,
            "mvp-th-2026-07",
            Start,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            transitions,
            18_000,
            0,
            450_000,
            "buyer-protection-test-v2",
            PhysicalQuote());
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();
        var payments = new RecordingPaymentIntentProvider();
        var handler = new PreparePaymentSheetHandler(
            repository,
            buyers,
            payments,
            new ConfiguredBuyerProtectionFeePolicy(
                new BuyerProtectionFeeOptions
                {
                    Enabled = true,
                    PolicyVersion = "buyer-protection-test-v2"
                }),
            db,
            new FixedClock(Start.AddMinutes(2)),
            transitions);

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(
                new PreparePaymentSheetCommand(
                    transaction.Id,
                    buyer.Id,
                    true),
                default));

        Assert.Equal(0, payments.CallCount);
        Assert.Equal(TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
    }

    [Theory]
    [InlineData(ParcelProtectionElectionStatus.Accepted, 479_000)]
    [InlineData(ParcelProtectionElectionStatus.Unavailable, 473_000)]
    public async Task Completed_booking_uses_final_protection_total_for_payment_intent(
        ParcelProtectionElectionStatus election,
        long expectedBuyerTotalSatang)
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new ToklongDbContext(options);
        var repository = new TransactionRepository(db);
        var buyers = new BuyerRepository(db);
        var transitions = new TransactionTransitionService();
        var buyer = BuyerAccount.Create("+66811111222", "ผู้ซื้อ ทดสอบ", "buyer-total@example.com", Start);
        await buyers.AddAsync(buyer, default);
        var transaction = TestTransactionFactory.CreateBuyerOffer(buyer.Id, buyer.FullName,
            buyer.PhoneNumber, FulfillmentType.PhysicalShipment, "กล้อง", "รายละเอียด",
            ConditionCode.UsedGood, "ไม่มี", null, 450_000, "mvp-th-2026-07", Start, transitions);
        var quote = PhysicalQuote();
        transaction.AcceptBuyerOffer(Guid.NewGuid(), "ผู้ขาย", "+66822222222", "KBANK",
            "ผู้ขาย", "1234567890", true, Start.AddMinutes(1), transitions,
            18_000, 0, 450_000, "buyer-protection-test-v2", quote);
        var accepted = election == ParcelProtectionElectionStatus.Accepted;
        transaction.RecordParcelProtectionElection(buyer.Id, new ParcelProtectionSelection(
            election, accepted ? 6_000 : 0, accepted ? 4_500 : 0,
            accepted ? 1_500 : 0, accepted ? 100_000 : 0,
            accepted ? 450_000 : 0, accepted ? "parcel-v1" : "parcel-unavailable-v1",
            accepted ? "option-1" : null, Start.AddMinutes(1), Start.AddHours(1)), Start.AddMinutes(1));
        var shipment = ManagedShipment.CreateOutbound(transaction.Id, new ManagedShipmentDraft(
            quote.Provider, "origin", "destination", "กล้อง", quote.WeightGrams,
            quote.WidthCentimeters, quote.LengthCentimeters, quote.HeightCentimeters,
            quote.CarrierCode, quote.ServiceCode, quote.ServiceName, quote.FeeSatang,
            accepted ? 4_500 : 0, accepted ? 450_000 : 0,
            accepted ? "FULL_VALUE" : null, quote.QuoteReference, quote.ExpiresAt,
            transaction.ParcelProtectionTermsVersion!, transaction.ParcelProtectionOptionReference,
            election, transaction.ParcelProtectionProviderCostSatang,
            transaction.ParcelProtectionIncludedCoverageSatang,
            transaction.ParcelProtectionSelectedCoverageSatang), Start.AddMinutes(1));
        var operation = ShippingOperation.Queue(transaction.Id, shipment.Id,
            ShippingOperationType.BookOutbound, "book-payment-total",
            ManagedShippingOperationQueue.BookingFingerprint(shipment), Start.AddMinutes(1));
        transaction.QueueManagedShipment(shipment, operation, ActorRole.System, "test", Start.AddMinutes(1));
        transaction.CompleteBuyerCheckoutShipmentBooking(shipment.Id, quote.Provider, "purchase", "tracking",
            null, quote.CarrierCode, quote.ServiceCode, quote.FeeSatang, accepted ? 4_500 : 0,
            accepted ? 450_000 : 0, accepted ? "FULL_VALUE" : null, Start.AddMinutes(2), Start.AddMinutes(2));
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();
        var payments = new RecordingPaymentIntentProvider();
        var handler = new PreparePaymentSheetHandler(repository, buyers, payments,
            new ConfiguredBuyerProtectionFeePolicy(new BuyerProtectionFeeOptions { Enabled = true, PolicyVersion = "buyer-protection-test-v2" }),
            db, new FixedClock(Start.AddMinutes(3)), transitions);

        await handler.Handle(new PreparePaymentSheetCommand(transaction.Id, buyer.Id, true), default);

        Assert.Equal(expectedBuyerTotalSatang, transaction.BuyerTotalSatang);
        Assert.Equal(expectedBuyerTotalSatang, payments.LastAmountSatang);
    }

    [Fact]
    public async Task Expired_payment_window_is_rejected_before_provider_call()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var repository = new TransactionRepository(db);
        var buyers = new BuyerRepository(db);
        var transitions = new TransactionTransitionService();
        var buyer = BuyerAccount.Create(
            "+66811111111",
            "ผู้ซื้อ ทดสอบ",
            "buyer@example.com",
            Start);
        await buyers.AddAsync(buyer, default);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            buyer.Id,
            buyer.FullName,
            buyer.PhoneNumber,
            FulfillmentType.DigitalHandoff,
            "รายละเอียดสินค้า",
            "รายการดิจิทัลที่ผู้ขายมีสิทธิ์โอน",
            ConditionCode.UsedGood,
            "ไม่มี",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            Start,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            transitions,
            18_000,
            0,
            450_000,
            "buyer-protection-test-v2");
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();
        var payments = new RecordingPaymentIntentProvider();
        var handler = new PreparePaymentSheetHandler(
            repository,
            buyers,
            payments,
            new ConfiguredBuyerProtectionFeePolicy(
                new BuyerProtectionFeeOptions
                {
                    Enabled = true,
                    PolicyVersion =
                        "buyer-protection-test-v2"
                }),
            db,
            new FixedClock(
                transaction.BuyerPaymentDeadlineAt!.Value),
            transitions);

        await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(
                new PreparePaymentSheetCommand(
                    transaction.Id,
                    buyer.Id,
                    true),
                default));

        Assert.Equal(0, payments.CallCount);
        Assert.Equal(TransactionState.Expired, transaction.State);
        Assert.Equal(
            TransactionExpirationReason.BuyerDidNotPay,
            transaction.ExpirationReason);
    }

    [Fact]
    public async Task Changed_fee_policy_is_rejected_before_provider_call()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var repository = new TransactionRepository(db);
        var buyers = new BuyerRepository(db);
        var transitions = new TransactionTransitionService();
        var buyer = BuyerAccount.Create(
            "+66811111112",
            "ผู้ซื้อ ทดสอบนโยบาย",
            "buyer-policy@example.com",
            Start);
        await buyers.AddAsync(buyer, default);
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            buyer.Id,
            buyer.FullName,
            buyer.PhoneNumber,
            FulfillmentType.DigitalHandoff,
            "รายละเอียดสินค้า",
            "รายการดิจิทัลที่ผู้ขายมีสิทธิ์โอน",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            450_000,
            "mvp-th-2026-07",
            Start,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Start.AddMinutes(1),
            transitions,
            18_000,
            0,
            450_000,
            "buyer-protection-test-v2");
        await repository.AddAsync(transaction, default);
        await db.SaveChangesAsync();
        var payments = new RecordingPaymentIntentProvider();
        var handler = new PreparePaymentSheetHandler(
            repository,
            buyers,
            payments,
            new ConfiguredBuyerProtectionFeePolicy(
                new BuyerProtectionFeeOptions
                {
                    Enabled = true,
                    PolicyVersion =
                        "buyer-protection-test-v3"
                }),
            db,
            new FixedClock(Start.AddMinutes(2)),
            transitions);

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(
                new PreparePaymentSheetCommand(
                    transaction.Id,
                    buyer.Id,
                    true),
                default));

        Assert.Contains("นโยบายค่าบริการเปลี่ยน", exception.Message);
        Assert.Equal(0, payments.CallCount);
        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            transaction.State);
    }

    private sealed class RecordingPaymentIntentProvider
        : IPaymentIntentProvider
    {
        public int CallCount { get; private set; }
        public long? LastAmountSatang { get; private set; }

        public Task<PaymentIntentPreparation> PrepareAsync(
            Guid transactionId,
            long amountSatang,
            string currency,
            FulfillmentType fulfillmentType,
            string receiptEmail,
            string? existingProviderReference,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastAmountSatang = amountSatang;
            return Task.FromResult(
                new PaymentIntentPreparation(
                    "pi_should_not_exist",
                    "secret",
                    "pk_test"));
        }
    }

    private static AcceptedShippingQuote PhysicalQuote() => new(
        TestTransactionFactory.ShippingOriginAddress,
        TestTransactionFactory.DeliveryProvinceName,
        TestTransactionFactory.DeliveryPostalCode,
        1_200,
        20,
        30,
        15,
        "development-shipping",
        "quote-001",
        "THAIPOST",
        "EMST",
        "EMS",
        5_000,
        0,
        0,
        null,
        Start.AddHours(2),
        TestTransactionFactory.DeliveryDistrictName,
        TestTransactionFactory.DeliverySubdistrictName,
        OriginAddressLine: TestTransactionFactory.ShippingOriginAddress);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }
}
