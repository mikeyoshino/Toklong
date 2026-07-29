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
    public void Shipment_without_full_value_insurance_is_rejected()
    {
        Assert.Throws<DomainException>(() =>
            ManagedShipment.CreateOutbound(
                Guid.NewGuid(),
                Draft() with
                {
                    InsuranceFeeSatang = 0,
                    InsuranceCode = ""
                },
                Now));
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
