using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Checkout.BeginCheckout;
using Toklong.Application.Features.Offers.CreateBuyerOffer;
using Toklong.Application.Features.Offers.RespondToBuyerOffer;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Pricing;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Offers;

public sealed class BuyerOfferFlowTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Checkout_is_blocked_until_authenticated_seller_accepts()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var transactions = new TransactionRepository(db);
        var buyers = new BuyerRepository(db);
        var sellers = new SellerRepository(db);
        var clock = new FixedClock(Now);
        var transitions = new TransactionTransitionService();

        var buyer = BuyerAccount.Create(
            "+66811111111",
            "ผู้ซื้อ ทดสอบ",
            "buyer@example.com",
            Now);
        await buyers.AddAsync(buyer, default);
        await db.SaveChangesAsync();

        var created = await new CreateBuyerOfferHandler(
            transactions,
            buyers,
            new BundledThaiAddressCatalog(),
            db,
            clock,
            PricingPolicy(),
            transitions).Handle(
            new CreateBuyerOfferCommand(
                buyer.Id,
                "0812345678",
                FulfillmentType.PhysicalShipment,
                "กล้องพร้อมเลนส์",
                "กล้องพร้อมเลนส์ มีรอยด้านข้าง",
                ConditionCode.UsedDefects,
                "มีรอยด้านข้าง",
                null,
                450_000,
                false,
                BangkokAddress(),
                true),
            default);

        Assert.Equal(
            "กล้องพร้อมเลนส์",
            created.ProductName);
        Assert.Equal(
            "+66812345678",
            created.SellerContact);
        Assert.Equal(
            "กรุงเทพมหานคร",
            created.DeliveryProvinceName);
        Assert.Equal(
            "10200",
            created.DeliveryPostalCode);
        Assert.Contains(
            "จังหวัด กรุงเทพมหานคร",
            created.DeliveryAddress);
        Assert.NotNull(
            buyer.GetSavedDeliveryAddress());
        Assert.Contains(
            created.AuditEvents,
            item => item.Name == "buyer_offer.created");

        var checkoutHandler = new BeginBuyerOfferCheckoutHandler(
            transactions,
            buyers,
            db,
            clock,
            transitions);
        await Assert.ThrowsAsync<DomainException>(() =>
            checkoutHandler.Handle(
                new BeginBuyerOfferCheckoutCommand(
                    created.BuyerAccessToken!,
                    buyer.Id,
                    true),
                default));

        var seller = SellerAccount.Create("+66812345678", Now);
        seller.UpdateSavedShippingOrigin(
            BangkokOrigin(),
            Now);
        var payout = seller.SavePayoutAccount(
            null, "KBANK", "ผู้ขาย ทดสอบ", "1234567890", Now);
        await sellers.AddAsync(seller, default);
        await db.SaveChangesAsync();

        var accepted = await new AcceptBuyerOfferHandler(
            transactions,
            sellers,
            new ConfiguredBuyerProtectionFeePolicy(
                new BuyerProtectionFeeOptions
                {
                    Enabled = true,
                    PolicyVersion =
                        "buyer-protection-test-v2"
                }),
            new TestShippingQuoteProvider(),
            new TestShippingQuoteProvider(),
            new BundledThaiAddressCatalog(),
            db,
            clock,
            transitions).Handle(
            new AcceptBuyerOfferCommand(
                created.PublicToken,
                seller.Id,
                payout.Id,
                true,
                true,
                18_000,
                0,
                450_000,
                "buyer-protection-test-v2",
                ShippingSelection()),
            default);

        Assert.Equal(
            TransactionState.SellerAcceptedAwaitingPayment,
            accepted.State);
        Assert.Equal(450_000, accepted.PriceSatang);
        Assert.Equal(5_000, accepted.ShippingFeeSatang);
        Assert.Equal(
            1_100,
            accepted.ParcelInsuranceFeeSatang);
        Assert.Equal(
            450_000,
            accepted.ShippingDeclaredValueSatang);
        Assert.Equal(
            "TEST_FULL_VALUE",
            accepted.ShippingInsuranceCode);
        Assert.Equal(474_100, accepted.BuyerTotalSatang);
        Assert.Equal(18_000, accepted.BuyerProtectionFeeSatang);
        Assert.Equal(0, accepted.PlatformFeeSatang);
        Assert.Equal(450_000, accepted.SellerExpectedNetSatang);
        Assert.Equal(
            BangkokOrigin().ToDisplayText(),
            accepted.ShippingOriginAddress);
        Assert.Equal(1_200, accepted.PackageWeightGrams);
        Assert.Equal("FLASH", accepted.CarrierCode);
        Assert.Equal(
            "buyer-protection-test-v2",
            accepted.FeePolicyVersion);
        Assert.Equal(72, accepted.ShipByDurationHours);
        Assert.NotNull(accepted.AgreementCoreSnapshotHash);
        var sellerAcceptance = Assert.Single(
            accepted.AgreementAcceptances);
        Assert.Equal(
            AgreementAcceptanceRole.Seller,
            sellerAcceptance.Role);
        Assert.Equal(
            accepted.AgreementCoreSnapshotHash,
            sellerAcceptance.AgreementCoreSnapshotHash);
        Assert.Null(accepted.PhotoUrl);

        var checkout = await checkoutHandler.Handle(
            new BeginBuyerOfferCheckoutCommand(
                created.BuyerAccessToken!,
                buyer.Id,
                true),
            default);

        Assert.Equal(TransactionState.PaymentPending, checkout.State);
        Assert.Equal(18_000, checkout.BuyerProtectionFeeSatang);
        Assert.Equal(0, checkout.PlatformFeeSatang);
        Assert.Equal(450_000, checkout.SellerExpectedNetSatang);
        Assert.NotNull(checkout.ProductSnapshotHash);
        Assert.Equal(
            2,
            checkout.AgreementAcceptances.Count);
        Assert.All(
            checkout.AgreementAcceptances,
            acceptance =>
                Assert.Equal(
                    checkout.AgreementCoreSnapshotHash,
                    acceptance.AgreementCoreSnapshotHash));
        var persisted = await transactions.GetByIdAsync(
            checkout.Id,
            default);
        Assert.NotNull(persisted);
        Assert.Contains(
            persisted.AgreementAcceptances,
            acceptance =>
                acceptance.Role ==
                AgreementAcceptanceRole.Buyer &&
                acceptance.ActorUserId == buyer.Id);
        Assert.Contains("จังหวัด กรุงเทพมหานคร", checkout.DeliveryAddress);
    }

    [Fact]
    public async Task Seller_cannot_accept_with_an_unowned_payout_account()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var transactions = new TransactionRepository(db);
        var buyers = new BuyerRepository(db);
        var sellers = new SellerRepository(db);
        var clock = new FixedClock(Now);
        var transitions = new TransactionTransitionService();

        var buyer = BuyerAccount.Create(
            "+66811111111",
            "ผู้ซื้อ ทดสอบ",
            "buyer@example.com",
            Now);
        await buyers.AddAsync(buyer, default);
        await db.SaveChangesAsync();

        var created = await new CreateBuyerOfferHandler(
            transactions,
            buyers,
            new BundledThaiAddressCatalog(),
            db,
            clock,
            PricingPolicy(),
            transitions).Handle(
            new CreateBuyerOfferCommand(
                buyer.Id,
                "0812345678",
                FulfillmentType.PhysicalShipment,
                "กล้องพร้อมสายคล้อง",
                "ใช้งานได้ปกติพร้อมสายคล้อง",
                ConditionCode.UsedGood, "",
                "https://example.com/buyer-photo.jpg",
                450_000,
                false,
                BangkokAddress(),
                false),
            default);
        var seller = SellerAccount.Create("+66812345678", Now);
        await sellers.AddAsync(seller, default);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<DomainException>(() =>
            new AcceptBuyerOfferHandler(
                transactions,
                sellers,
                new ConfiguredBuyerProtectionFeePolicy(
                    new BuyerProtectionFeeOptions()),
                new TestShippingQuoteProvider(),
                new TestShippingQuoteProvider(),
                new BundledThaiAddressCatalog(),
                db,
                clock,
                transitions).Handle(
                new AcceptBuyerOfferCommand(
                    created.PublicToken,
                    seller.Id,
                    Guid.NewGuid(),
                    true,
                    true,
                    0,
                    0,
                    450_000,
                    "payments-disabled"),
                default));
    }

    [Fact]
    public async Task Different_seller_cannot_accept_or_decline_targeted_offer()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var transactions = new TransactionRepository(db);
        var buyers = new BuyerRepository(db);
        var sellers = new SellerRepository(db);
        var transitions = new TransactionTransitionService();
        var clock = new FixedClock(Now);
        var buyer = BuyerAccount.Create(
            "+66811111111",
            "ผู้ซื้อ ทดสอบ",
            "buyer@example.com",
            Now);
        await buyers.AddAsync(buyer, default);
        var otherSeller = SellerAccount.Create(
            "+66899999999", Now);
        var payout = otherSeller.SavePayoutAccount(
            null,
            "KBANK",
            "ผู้ขาย คนอื่น",
            "1234567890",
            Now);
        await sellers.AddAsync(otherSeller, default);
        await db.SaveChangesAsync();
        var created = await new CreateBuyerOfferHandler(
            transactions,
            buyers,
            new BundledThaiAddressCatalog(),
            db,
            clock,
            PricingPolicy(),
            transitions).Handle(
            new CreateBuyerOfferCommand(
                buyer.Id,
                "0812345678",
                FulfillmentType.PhysicalShipment,
                "กล้องพร้อมเลนส์",
                "กล้องพร้อมเลนส์ ใช้งานได้ปกติ",
                ConditionCode.UsedGood,
                "",
                "https://example.com/buyer-photo.jpg",
                450_000,
                false,
                BangkokAddress(),
                false),
            default);

        await Assert.ThrowsAsync<
            Toklong.Application.Common.ForbiddenException>(() =>
            new AcceptBuyerOfferHandler(
                transactions,
                sellers,
                new ConfiguredBuyerProtectionFeePolicy(
                    new BuyerProtectionFeeOptions()),
                new TestShippingQuoteProvider(),
                new TestShippingQuoteProvider(),
                new BundledThaiAddressCatalog(),
                db,
                clock,
                transitions).Handle(
                new AcceptBuyerOfferCommand(
                    created.PublicToken,
                    otherSeller.Id,
                    payout.Id,
                    true,
                    true,
                    0,
                    0,
                    450_000,
                    "payments-disabled"),
                default));

        await Assert.ThrowsAsync<
            Toklong.Application.Common.ForbiddenException>(() =>
            new DeclineBuyerOfferHandler(
                transactions,
                sellers,
                db,
                clock,
                transitions).Handle(
                new DeclineBuyerOfferCommand(
                    created.PublicToken,
                    otherSeller.Id),
                default));

        var unchanged = await transactions.GetByIdAsync(
            created.Id,
            default);
        Assert.NotNull(unchanged);
        Assert.Equal(
            TransactionState.AwaitingSellerAcceptance,
            unchanged.State);
        Assert.Null(unchanged.SellerId);
    }

    [Fact]
    public async Task Offer_creation_requires_a_persisted_buyer_account()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var handler = new CreateBuyerOfferHandler(
            new TransactionRepository(db),
            new BuyerRepository(db),
            new BundledThaiAddressCatalog(),
            db,
            new FixedClock(Now),
            PricingPolicy(),
            new TransactionTransitionService());

        await Assert.ThrowsAsync<Toklong.Application.Common.NotFoundException>(
            () => handler.Handle(
                new CreateBuyerOfferCommand(
                    Guid.NewGuid(),
                    "0812345678",
                    FulfillmentType.PhysicalShipment,
                    "กล้องพร้อมเลนส์",
                    "กล้องพร้อมเลนส์ ใช้งานได้ปกติ",
                    ConditionCode.UsedGood,
                    "",
                    "https://example.com/buyer-photo.jpg",
                    450_000,
                    false,
                    BangkokAddress(),
                    false),
                default));
    }

    [Fact]
    public async Task Offer_creation_rejects_price_above_active_pilot_limit()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var buyer = BuyerAccount.Create(
            "+66811111111",
            "ผู้ซื้อ ทดสอบ",
            "buyer@example.com",
            Now);
        await db.Buyers.AddAsync(buyer);
        await db.SaveChangesAsync();
        var handler = new CreateBuyerOfferHandler(
            new TransactionRepository(db),
            new BuyerRepository(db),
            new BundledThaiAddressCatalog(),
            db,
            new FixedClock(Now),
            PricingPolicy(),
            new TransactionTransitionService());

        var exception = await Assert.ThrowsAsync<DomainException>(() =>
            handler.Handle(
                new CreateBuyerOfferCommand(
                    buyer.Id,
                    "0812345678",
                    FulfillmentType.PhysicalShipment,
                    "กล้องพร้อมเลนส์",
                    "กล้องพร้อมเลนส์ ใช้งานได้ปกติ",
                    ConditionCode.UsedGood,
                    "",
                    null,
                    3_000_001,
                    false,
                    BangkokAddress(),
                    false),
                default));

        Assert.Contains("1,000–30,000", exception.Message);
        Assert.Empty(db.Transactions);
    }

    private static ConfiguredBuyerProtectionFeePolicy PricingPolicy() =>
        new(
            new BuyerProtectionFeeOptions
            {
                Enabled = true,
                PolicyVersion = "buyer-protection-test-v2"
            });

    [Fact]
    public async Task Persisted_agreement_acceptance_cannot_be_updated_or_deleted()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        await using var db = new ToklongDbContext(options);
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "+66822222222",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานได้ปกติ มีรอยตามรูป",
            ConditionCode.UsedDefects,
            "มีรอยด้านข้าง",
            "https://example.com/photo.jpg",
            450_000,
            "mvp-th-2026-07",
            Now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddMinutes(1),
            transitions,
            shipping: TestTransactionFactory.ShippingQuote(
                Now.AddMinutes(1)));
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        var acceptance = Assert.Single(
            transaction.AgreementAcceptances);
        db.Entry(acceptance).State = EntityState.Modified;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => db.SaveChangesAsync());
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private static OfferDeliveryAddressInput
        BangkokAddress() =>
        new(
            "123 ถนนตัวอย่าง",
            1,
            1001,
            100101);

    private static SellerShippingOriginAddress
        BangkokOrigin() =>
        new(
            "123 ถนนตัวอย่าง",
            1,
            "กรุงเทพมหานคร",
            1001,
            "พระนคร",
            100101,
            "พระบรมมหาราชวัง",
            "10200");

    private static SellerShippingSelectionInput
        ShippingSelection() =>
        new(
            true,
            null,
            null,
            null,
            null,
            false,
            1_200,
            20,
            30,
            15,
            "quote-test",
            5_000);

    private sealed class TestShippingQuoteProvider
        : IShippingQuoteProvider, IShipmentProvider
    {
        public string ProviderName => "test-shipping";

        public Task<IReadOnlyList<ShippingQuoteOption>>
            GetQuotesAsync(
                ShippingQuoteRequest request,
                CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ShippingQuoteOption>>(
                [Quote()]);

        public Task<ShippingQuoteOption> ValidateQuoteAsync(
            ShippingQuoteRequest request,
            string quoteReference,
            long disclosedFeeSatang,
            CancellationToken cancellationToken) =>
            Task.FromResult(Quote());

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new ShipmentReservation(
                    ProviderName,
                    "test-purchase",
                    "TESTSP12345678",
                    "TH123456789012",
                    request.Quote.CarrierCode,
                    request.Quote.ServiceCode,
                    request.Quote.FeeSatang,
                    request.Quote.InsuranceFeeSatang,
                    request.Quote.DeclaredValueSatang,
                    request.Quote.InsuranceCode,
                    Now));

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentConfirmation> ConfirmAsync(
            string purchaseReference,
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> GetLabelHtmlAsync(
            ShipmentLabelRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private static ShippingQuoteOption Quote() =>
            new(
                "test-shipping",
                "quote-test",
                "FLASH",
                "STANDARD",
                "Flash Express Standard",
                5_000,
                1_100,
                450_000,
                "TEST_FULL_VALUE",
                Now.AddHours(2));
    }
}
