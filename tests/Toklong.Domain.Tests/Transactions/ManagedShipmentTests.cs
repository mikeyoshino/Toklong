using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class ManagedShipmentTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Outbound_shipment_keeps_an_immutable_insured_dropoff_snapshot()
    {
        var transactionId = Guid.NewGuid();
        var shipment = ManagedShipment.CreateOutbound(
            transactionId,
            Draft(),
            Now);

        Assert.Equal(transactionId, shipment.TransactionId);
        Assert.Equal(
            ShipmentDirection.Outbound,
            shipment.Direction);
        Assert.Equal(
            ManagedShipmentStatus.PendingBooking,
            shipment.Status);
        Assert.Equal("DropOff", shipment.HandoffMode);
        Assert.Equal(5_200, shipment.BaseShippingFeeSatang);
        Assert.Equal(1_100, shipment.InsuranceFeeSatang);
        Assert.Equal(120_000, shipment.DeclaredValueSatang);
        Assert.Equal("FULL_VALUE", shipment.InsuranceCode);
    }

    [Fact]
    public void Return_has_its_own_identity_and_direction()
    {
        var transactionId = Guid.NewGuid();
        var outbound = ManagedShipment.CreateOutbound(
            transactionId,
            Draft(),
            Now);
        var returned = ManagedShipment.CreateReturn(
            transactionId,
            Draft() with
            {
                OriginPrivateSnapshotReference =
                    "buyer-return-origin",
                DestinationPrivateSnapshotReference =
                    "seller-return-destination"
            },
            Now.AddMinutes(1));

        Assert.NotEqual(outbound.Id, returned.Id);
        Assert.Equal(
            ShipmentDirection.Return,
            returned.Direction);
        Assert.Null(returned.PurchaseReference);
        Assert.Null(returned.ProviderTrackingCode);
    }

    [Fact]
    public void Shipment_with_incomplete_optional_protection_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            ManagedShipment.CreateOutbound(
                Guid.NewGuid(),
                Draft() with
                {
                    InsuranceFeeSatang = 1_100,
                    InsuranceCode = ""
                },
                Now));
    }

    [Fact]
    public void Outbound_booking_can_use_included_coverage_without_optional_fee()
    {
        var shipment = ManagedShipment.CreateOutbound(
            Guid.NewGuid(),
            Draft() with
            {
                InsuranceFeeSatang = 0,
                DeclaredValueSatang = 100_000,
                InsuranceCode = null
            },
            Now);

        Assert.Equal(0, shipment.InsuranceFeeSatang);
        Assert.Equal(100_000, shipment.DeclaredValueSatang);
        Assert.Null(shipment.InsuranceCode);
    }

    [Fact]
    public void Insurance_tuple_must_be_all_zero_or_fully_populated()
    {
        Assert.Throws<DomainException>(() =>
            ManagedShipment.CreateOutbound(
                Guid.NewGuid(),
                Draft() with
                {
                    InsuranceFeeSatang = 4_500,
                    DeclaredValueSatang = 450_000,
                    InsuranceCode = null
                },
                Now));
    }

    [Fact]
    public void Superseded_operation_cannot_be_claimed()
    {
        var operation = ShippingOperation.Queue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShippingOperationType.BookOutbound,
            "book-outbound:test",
            new string('a', 64),
            Now);
        operation.Claim("worker", Now, TimeSpan.FromMinutes(5));

        operation.Supersede(
            "worker",
            "parcel-protection-quote-changed",
            Now.AddSeconds(1));

        Assert.Equal(
            ShippingOperationStatus.Superseded,
            operation.Status);
        Assert.Throws<DomainException>(() =>
            operation.Claim(
                "worker",
                Now.AddMinutes(6),
                TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public void Reservation_and_confirmation_are_recorded_in_order()
    {
        var shipment = ManagedShipment.CreateOutbound(
            Guid.NewGuid(),
            Draft(),
            Now);

        shipment.RecordReservation(
            "purchase-001",
            "provider-track-001",
            null,
            Now.AddMinutes(1));
        shipment.RecordConfirmation(
            "courier-track-001",
            "booking",
            Now.AddMinutes(2));

        Assert.Equal(
            ManagedShipmentStatus.Confirmed,
            shipment.Status);
        Assert.Equal("purchase-001", shipment.PurchaseReference);
        Assert.Equal(
            "courier-track-001",
            shipment.CourierTrackingCode);
        Assert.Equal(2, shipment.Version);
    }

    [Fact]
    public void Authorized_resolution_closes_exception_and_new_evidence_reopens_it()
    {
        var shipment = ManagedShipment.CreateOutbound(
            Guid.NewGuid(),
            Draft(),
            Now);
        shipment.RecordCarrierException(
            "problem",
            Now.AddMinutes(1));

        Assert.True(shipment.HasOpenException);

        shipment.ResolveException(
            "crm-user",
            "CASE-SHIP-001",
            Now.AddMinutes(2));

        Assert.False(shipment.HasOpenException);
        Assert.Equal(
            "CASE-SHIP-001",
            shipment.ExceptionResolutionReference);

        shipment.RecordCarrierException(
            "return_problem",
            Now.AddMinutes(3));

        Assert.True(shipment.HasOpenException);
        Assert.Null(shipment.ExceptionResolvedAt);
    }

    [Fact]
    public void Resume_tracking_review_keeps_release_blocked()
    {
        var shipment = ManagedShipment.CreateOutbound(
            Guid.NewGuid(),
            Draft(),
            Now);
        shipment.RecordCarrierException(
            "problem",
            Now.AddMinutes(1));

        shipment.ResumeTrackingReview(Now.AddMinutes(2));

        Assert.Equal(
            ManagedShipmentStatus.TrackingUnverified,
            shipment.Status);
        Assert.True(shipment.HasOpenException);
    }

    private static ManagedShipmentDraft Draft() =>
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
}
