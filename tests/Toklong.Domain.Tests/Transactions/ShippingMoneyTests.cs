using System.Text.Json;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class ShippingMoneyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Insured_physical_offer_freezes_separate_buyer_costs_without_changing_seller_net()
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            FulfillmentType.PhysicalShipment,
            "กล้องทดสอบ",
            "กล้องพร้อมเลนส์และอุปกรณ์",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            120_000,
            "terms-v1",
            Now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddMinutes(1),
            transitions,
            buyerProtectionFeeSatang: 5_900,
            sellerExpectedNetSatang: 120_000,
            feePolicyVersion: "buyer-protection-test-v1",
            shipping: new AcceptedShippingQuote(
                TestTransactionFactory.ShippingOriginAddress,
                TestTransactionFactory.DeliveryProvinceName,
                TestTransactionFactory.DeliveryPostalCode,
                1_200,
                20,
                30,
                15,
                "test-shipping",
                "insured-quote",
                "FLASH",
                "STANDARD",
                "Flash Express Standard",
                5_200,
                1_100,
                120_000,
                "FULL_VALUE",
                Now.AddHours(2)));

        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            Now.AddMinutes(2),
            transitions,
            buyerProtectionFeeSatang: 5_900,
            sellerExpectedNetSatang: 120_000,
            feePolicyVersion: "buyer-protection-test-v1");

        Assert.Equal(5_200, transaction.ShippingFeeSatang);
        Assert.Equal(
            1_100,
            transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(
            120_000,
            transaction.ShippingDeclaredValueSatang);
        Assert.Equal(
            "FULL_VALUE",
            transaction.ShippingInsuranceCode);
        Assert.Equal(132_200, transaction.BuyerTotalSatang);
        Assert.Equal(
            120_000,
            transaction.SellerExpectedNetSatang);
        Assert.Equal(10, transaction.SnapshotSchemaVersion);

        using var snapshot = JsonDocument.Parse(
            transaction.ProductSnapshotJson!);
        Assert.Equal(
            1_100,
            snapshot.RootElement
                .GetProperty("ParcelInsuranceFeeSatang")
                .GetInt64());
        Assert.Equal(
            120_000,
            snapshot.RootElement
                .GetProperty("ShippingDeclaredValueSatang")
                .GetInt64());
    }

    [Fact]
    public void Insurance_below_full_item_value_is_rejected()
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66899999999",
            FulfillmentType.PhysicalShipment,
            "กล้องทดสอบ",
            "กล้องพร้อมเลนส์และอุปกรณ์",
            ConditionCode.UsedGood,
            "ไม่มี",
            null,
            120_000,
            "terms-v1",
            Now,
            transitions);

        Assert.Throws<DomainException>(() =>
            transaction.AcceptBuyerOffer(
                Guid.NewGuid(),
                "ผู้ขาย ทดสอบ",
                "+66811111111",
                "KBANK",
                "ผู้ขาย ทดสอบ",
                "1234567890",
                true,
                Now.AddMinutes(1),
                transitions,
                shipping: new AcceptedShippingQuote(
                    TestTransactionFactory.ShippingOriginAddress,
                    TestTransactionFactory.DeliveryProvinceName,
                    TestTransactionFactory.DeliveryPostalCode,
                    1_200,
                    20,
                    30,
                    15,
                    "test-shipping",
                    "underinsured-quote",
                    "FLASH",
                    "STANDARD",
                    "Flash Express Standard",
                    5_200,
                    1_100,
                    119_999,
                    "FULL_VALUE",
                    Now.AddHours(2))));
    }

    [Fact]
    public void Included_coverage_does_not_charge_or_prompt()
    {
        var transaction = AcceptedPhysicalOffer(100_000);

        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            Selection(
                ParcelProtectionElectionStatus.NotApplicable,
                includedCoverageLimitSatang: 100_000,
                selectedCoverageLimitSatang: 100_000),
            Now.AddMinutes(2));

        Assert.Equal(
            ParcelProtectionElectionStatus.NotApplicable,
            transaction.ParcelProtectionElection);
        Assert.Equal(0, transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(
            transaction.PriceSatang +
            transaction.ShippingFeeSatang +
            transaction.BuyerProtectionFeeSatang,
            transaction.BuyerTotalSatang);
    }

    [Fact]
    public void Accepted_add_on_stores_internal_split_and_combined_buyer_price()
    {
        var transaction = AcceptedPhysicalOffer(450_000);

        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            Selection(
                ParcelProtectionElectionStatus.Accepted,
                customerPriceSatang: 6_000,
                providerCostSatang: 4_500,
                toklongServiceFeeSatang: 1_500,
                includedCoverageLimitSatang: 100_000,
                selectedCoverageLimitSatang: 450_000,
                providerOptionReference: "protected-option"),
            Now.AddMinutes(2));

        Assert.Equal(6_000, transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(4_500, transaction.ParcelProtectionProviderCostSatang);
        Assert.Equal(1_500, transaction.ParcelProtectionServiceFeeSatang);
        Assert.Equal(450_000, transaction.ParcelProtectionSelectedCoverageSatang);
        Assert.Equal(
            transaction.PriceSatang +
            transaction.ShippingFeeSatang +
            transaction.BuyerProtectionFeeSatang +
            6_000,
            transaction.BuyerTotalSatang);
        Assert.Equal(
            transaction.PriceSatang - transaction.PlatformFeeSatang,
            transaction.SellerExpectedNetSatang);
    }

    [Fact]
    public void Declined_add_on_keeps_included_coverage_without_charge()
    {
        var transaction = AcceptedPhysicalOffer(450_000);

        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            Selection(
                ParcelProtectionElectionStatus.Declined,
                includedCoverageLimitSatang: 100_000,
                selectedCoverageLimitSatang: 100_000),
            Now.AddMinutes(2));

        Assert.Equal(0, transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(100_000, transaction.ParcelProtectionSelectedCoverageSatang);
    }

    [Fact]
    public void Seller_or_changed_terms_cannot_write_the_buyer_annex()
    {
        var transaction = AcceptedPhysicalOffer(450_000);
        var selection = Selection(
            ParcelProtectionElectionStatus.Accepted,
            customerPriceSatang: 6_000,
            providerCostSatang: 4_500,
            toklongServiceFeeSatang: 1_500,
            includedCoverageLimitSatang: 100_000,
            selectedCoverageLimitSatang: 450_000,
            providerOptionReference: "protected-option");

        Assert.Throws<DomainException>(() =>
            transaction.RecordParcelProtectionElection(
                Guid.NewGuid(), selection, Now.AddMinutes(2)));

        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value, selection, Now.AddMinutes(2));

        Assert.Throws<DomainException>(() =>
            transaction.RecordParcelProtectionElection(
                transaction.BuyerId.Value,
                selection with { CustomerPriceSatang = 6_100 },
                Now.AddMinutes(3)));
    }

    [Theory]
    [InlineData(5_999, 4_500, 1_500, 100_000, 450_000)]
    [InlineData(6_000, 4_500, 1_500, 0, 450_000)]
    [InlineData(6_000, 4_500, 1_500, 100_000, 0)]
    [InlineData(6_000, 4_500, 1_500, 100_000, 99_999)]
    public void Accepted_add_on_rejects_invalid_money_or_coverage(
        long customerPriceSatang,
        long providerCostSatang,
        long serviceFeeSatang,
        long includedCoverageLimitSatang,
        long selectedCoverageLimitSatang)
    {
        var transaction = AcceptedPhysicalOffer(450_000);

        Assert.Throws<DomainException>(() =>
            transaction.RecordParcelProtectionElection(
                transaction.BuyerId!.Value,
                Selection(
                    ParcelProtectionElectionStatus.Accepted,
                    customerPriceSatang,
                    providerCostSatang,
                    serviceFeeSatang,
                    includedCoverageLimitSatang,
                    selectedCoverageLimitSatang,
                    "protected-option"),
                Now.AddMinutes(2)));
    }

    [Fact]
    public void Accepted_add_on_rejects_an_option_that_expires_after_payment_deadline()
    {
        var transaction = AcceptedPhysicalOffer(450_000);

        Assert.Throws<DomainException>(() =>
            transaction.RecordParcelProtectionElection(
                transaction.BuyerId!.Value,
                Selection(
                    ParcelProtectionElectionStatus.Accepted,
                    6_000, 4_500, 1_500, 100_000, 450_000,
                    "protected-option",
                    expiresAt: transaction.BuyerPaymentDeadlineAt!.Value.AddTicks(1)),
                Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData(ParcelProtectionElectionStatus.Declined)]
    [InlineData(ParcelProtectionElectionStatus.NotApplicable)]
    [InlineData(ParcelProtectionElectionStatus.Unavailable)]
    public void Non_accepted_elections_reject_charges_and_provider_reference(
        ParcelProtectionElectionStatus election)
    {
        var charged = AcceptedPhysicalOffer(450_000);
        Assert.Throws<DomainException>(() =>
            charged.RecordParcelProtectionElection(
                charged.BuyerId!.Value,
                Selection(election, customerPriceSatang: 1),
                Now.AddMinutes(2)));

        var referenced = AcceptedPhysicalOffer(450_000);
        Assert.Throws<DomainException>(() =>
            referenced.RecordParcelProtectionElection(
                referenced.BuyerId!.Value,
                Selection(election, providerOptionReference: "not-allowed"),
                Now.AddMinutes(2)));
    }

    [Fact]
    public void Unavailable_may_preserve_uncertified_zero_coverage_only()
    {
        var transaction = AcceptedPhysicalOffer(450_000);

        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            Selection(
                ParcelProtectionElectionStatus.Unavailable,
                includedCoverageLimitSatang: 0,
                selectedCoverageLimitSatang: 0),
            Now.AddMinutes(2));

        Assert.Equal(0, transaction.ParcelProtectionIncludedCoverageSatang);
        Assert.Equal(0, transaction.ParcelProtectionSelectedCoverageSatang);
    }

    [Fact]
    public void Parcel_protection_availability_audit_is_idempotent_and_election_freezes_at_checkout()
    {
        var transaction = AcceptedPhysicalOffer(450_000);
        var buyerId = transaction.BuyerId!.Value;

        transaction.RecordParcelProtectionAvailabilityPresented(
            buyerId, true, "parcel-protection-offered", Now.AddMinutes(2));
        transaction.RecordParcelProtectionAvailabilityPresented(
            buyerId, true, "parcel-protection-offered", Now.AddMinutes(3));
        Assert.Single(transaction.AuditEvents, audit =>
            audit.Name == "parcel_protection.offered");

        transaction.RecordParcelProtectionElection(
            buyerId,
            Selection(
                ParcelProtectionElectionStatus.Accepted,
                6_000, 4_500, 1_500, 100_000, 450_000,
                "protected-option"),
            Now.AddMinutes(2));
        transaction.BeginCheckout(
            "ผู้ซื้อ",
            "buyer@example.com",
            Now.AddMinutes(5),
            new TransactionTransitionService());

        using var coreSnapshot = JsonDocument.Parse(
            transaction.AgreementCoreSnapshotJson!);
        Assert.False(coreSnapshot.RootElement.TryGetProperty(
            "ParcelProtection", out _));
        using var productSnapshot = JsonDocument.Parse(
            transaction.ProductSnapshotJson!);
        Assert.Equal(
            "Accepted",
            productSnapshot.RootElement
                .GetProperty("ParcelProtection")
                .GetProperty("ParcelProtectionElection")
                .GetString());

        Assert.Throws<DomainException>(() =>
            transaction.RecordParcelProtectionElection(
                buyerId,
                Selection(
                    ParcelProtectionElectionStatus.Declined,
                    includedCoverageLimitSatang: 100_000,
                    selectedCoverageLimitSatang: 100_000),
                Now.AddMinutes(6)));
    }

    [Fact]
    public void Invalidating_an_election_preserves_included_coverage_and_requires_reconfirmation()
    {
        var transaction = AcceptedPhysicalOffer(450_000);
        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            Selection(
                ParcelProtectionElectionStatus.Accepted,
                6_000, 4_500, 1_500, 100_000, 450_000,
                "protected-option"),
            Now.AddMinutes(2));

        transaction.InvalidateParcelProtectionElection(
            "provider_option_changed", Now.AddMinutes(3));

        Assert.Equal(
            ParcelProtectionElectionStatus.ReconfirmationRequired,
            transaction.ParcelProtectionElection);
        Assert.Equal(0, transaction.ParcelInsuranceFeeSatang);
        Assert.Equal(100_000, transaction.ParcelProtectionIncludedCoverageSatang);
        Assert.Equal(0, transaction.ParcelProtectionSelectedCoverageSatang);
        Assert.Null(transaction.ParcelProtectionOptionReference);
        Assert.Equal(
            transaction.PriceSatang +
            transaction.ShippingFeeSatang +
            transaction.BuyerProtectionFeeSatang,
            transaction.BuyerTotalSatang);
        Assert.Single(transaction.AuditEvents, audit =>
            audit.Name == "parcel_protection.reconfirmation_required");
    }

    [Fact]
    public void Invalidating_an_election_after_the_payment_deadline_is_rejected()
    {
        var transaction = AcceptedPhysicalOffer(450_000);
        transaction.RecordParcelProtectionElection(
            transaction.BuyerId!.Value,
            Selection(
                ParcelProtectionElectionStatus.Accepted,
                6_000, 4_500, 1_500, 100_000, 450_000,
                "protected-option"),
            Now.AddMinutes(2));

        Assert.Throws<DomainException>(() =>
            transaction.InvalidateParcelProtectionElection(
                "provider_option_changed",
                transaction.BuyerPaymentDeadlineAt!.Value));
    }

    [Fact]
    public void Parcel_protection_booking_ready_requires_a_reserved_physical_shipment()
    {
        var unreserved = AcceptedPhysicalOffer(450_000);
        Assert.False(unreserved.ParcelProtectionBookingReady);

        var transitions = new TransactionTransitionService();
        var reserved = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(), "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment, "กล้องทดสอบ", "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood, "ไม่มี", null, 450_000, "terms-v1", Now,
            transitions);
        reserved.AcceptBuyerOffer(
            Guid.NewGuid(), "ผู้ขาย", "+66811111111", "KBANK", "ผู้ขาย",
            "1234567890", true, Now.AddMinutes(1), transitions,
            shipping: TestTransactionFactory.ShippingQuote(Now.AddMinutes(1)) with
            {
                ReservedAt = Now.AddMinutes(1)
            });

        Assert.True(reserved.ParcelProtectionBookingReady);
    }

    private static SaleTransaction AcceptedPhysicalOffer(long priceSatang)
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(), "ผู้ซื้อ", "buyer@example.com",
            FulfillmentType.PhysicalShipment, "กล้องทดสอบ", "กล้องพร้อมเลนส์",
            ConditionCode.UsedGood, "ไม่มี", null, priceSatang, "terms-v1", Now,
            transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(), "ผู้ขาย", "+66811111111", "KBANK", "ผู้ขาย",
            "1234567890", true, Now.AddMinutes(1), transitions,
            shipping: TestTransactionFactory.ShippingQuote(Now.AddMinutes(1)));
        return transaction;
    }

    private static ParcelProtectionSelection Selection(
        ParcelProtectionElectionStatus election,
        long customerPriceSatang = 0,
        long providerCostSatang = 0,
        long toklongServiceFeeSatang = 0,
        long includedCoverageLimitSatang = 100_000,
        long selectedCoverageLimitSatang = 100_000,
        string? providerOptionReference = null,
        DateTimeOffset? expiresAt = null) =>
        new(
            election,
            customerPriceSatang,
            providerCostSatang,
            toklongServiceFeeSatang,
            includedCoverageLimitSatang,
            selectedCoverageLimitSatang,
            "parcel-protection-2026-07-30",
            providerOptionReference,
            Now,
            expiresAt ?? Now.AddMinutes(30));
}
