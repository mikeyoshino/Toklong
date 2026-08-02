using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class ShipmentCounterQrResourceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 8, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Confirmed_outbound_can_queue_one_pending_counter_qr()
    {
        var shipment = ConfirmedShipment(ShipmentDirection.Outbound);

        var resource = shipment.QueueCounterQr(Now);
        var replay = shipment.QueueCounterQr(Now.AddSeconds(1));

        Assert.Same(resource, replay);
        Assert.Same(resource, shipment.CounterQrResource);
        Assert.Equal(CounterQrResourceStatus.Pending, resource.Status);
        Assert.Equal(shipment.Id, resource.ManagedShipmentId);
        Assert.Equal(shipment.TransactionId, resource.TransactionId);
        Assert.Equal(Now, resource.NextAttemptAt);
    }

    [Fact]
    public void Resource_rejects_ready_without_protected_artifact_and_sha256()
    {
        var resource = ClaimedResource();

        Assert.Throws<DomainException>(() =>
            resource.RecordReady(
                CounterQrRepresentation.ProviderPng,
                [],
                "aspnet-dp:v1",
                new string('a', 64),
                new string('b', 64),
                null,
                Now.AddSeconds(1),
                "worker-a"));
        Assert.Throws<DomainException>(() =>
            resource.RecordReady(
                CounterQrRepresentation.ProviderPng,
                [1, 2, 3],
                "aspnet-dp:v1",
                "not-a-sha",
                new string('b', 64),
                null,
                Now.AddSeconds(1),
                "worker-a"));
    }

    [Fact]
    public void Ready_resource_keeps_only_protected_artifact_and_safe_metadata()
    {
        var resource = ClaimedResource();
        var protectedArtifact = Enumerable
            .Repeat((byte)7, 64)
            .ToArray();

        resource.RecordReady(
            CounterQrRepresentation.ProviderPng,
            protectedArtifact,
            "aspnet-dp:v1",
            new string('a', 64),
            new string('b', 64),
            Now.AddHours(1),
            Now.AddSeconds(1),
            "worker-a");

        Assert.Equal(CounterQrResourceStatus.Ready, resource.Status);
        Assert.Equal(protectedArtifact, resource.ProtectedArtifact);
        Assert.Equal("aspnet-dp:v1", resource.ProtectionVersion);
        Assert.Equal(new string('a', 64), resource.ArtifactSha256);
        Assert.Null(resource.LeaseOwner);
        Assert.Null(resource.LastSanitizedErrorCode);
    }

    [Fact]
    public void Retryable_failure_schedules_only_the_resource_read()
    {
        var shipment = ConfirmedShipment(ShipmentDirection.Outbound);
        var resource = shipment.QueueCounterQr(Now);
        resource.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(1));

        resource.RecordRetryableError(
            "provider-temporary",
            Now.AddMinutes(2),
            Now.AddSeconds(1),
            "worker-a");

        Assert.Equal(
            CounterQrResourceStatus.RetryableError,
            resource.Status);
        Assert.Equal(Now.AddMinutes(2), resource.NextAttemptAt);
        Assert.Equal("provider-temporary", resource.LastSanitizedErrorCode);
        Assert.Equal(ManagedShipmentStatus.Confirmed, shipment.Status);
    }

    [Fact]
    public void Manual_retry_is_idempotent_and_does_not_change_shipment_status()
    {
        var shipment = ConfirmedShipment(ShipmentDirection.Outbound);
        var resource = shipment.QueueCounterQr(Now);
        resource.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(1));
        resource.RecordRetryableError(
            "provider-temporary",
            Now.AddMinutes(2),
            Now.AddSeconds(1),
            "worker-a");

        Assert.True(resource.RequestRetry(Now.AddSeconds(2)));
        Assert.False(resource.RequestRetry(Now.AddSeconds(3)));
        Assert.Equal(CounterQrResourceStatus.Pending, resource.Status);
        Assert.Equal(Now.AddSeconds(2), resource.NextAttemptAt);
        Assert.Equal(ManagedShipmentStatus.Confirmed, shipment.Status);
    }

    [Fact]
    public void Expired_ready_resource_can_be_claimed_for_a_safe_refresh()
    {
        var resource = ClaimedResource();
        resource.RecordReady(
            CounterQrRepresentation.ProviderPng,
            Enumerable.Repeat((byte)7, 64).ToArray(),
            "aspnet-dp:v1",
            new string('a', 64),
            new string('b', 64),
            Now.AddMinutes(2),
            Now.AddSeconds(1),
            "worker-a");

        resource.Claim(
            "worker-b",
            Now.AddMinutes(2),
            TimeSpan.FromMinutes(1));

        Assert.Equal(CounterQrResourceStatus.Pending, resource.Status);
        Assert.Null(resource.ProtectedArtifact);
        Assert.Equal("worker-b", resource.LeaseOwner);
        Assert.Equal(2, resource.AttemptCount);
    }

    [Theory]
    [InlineData(ShipmentDirection.Outbound, false)]
    [InlineData(ShipmentDirection.Return, true)]
    public void Return_or_unconfirmed_shipment_cannot_queue_counter_qr(
        ShipmentDirection direction,
        bool confirm)
    {
        var shipment = NewShipment(direction);
        shipment.RecordReservation(
            "purchase-1",
            "provider-track-1",
            null,
            Now.AddMinutes(-2));
        if (confirm)
            shipment.RecordConfirmation(
                "courier-track-1",
                "booking",
                Now.AddMinutes(-1));

        Assert.Throws<DomainException>(() =>
            shipment.QueueCounterQr(Now));
    }

    private static ShipmentCounterQrResource ClaimedResource()
    {
        var resource = ConfirmedShipment(ShipmentDirection.Outbound)
            .QueueCounterQr(Now);
        resource.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(1));
        return resource;
    }

    private static ManagedShipment ConfirmedShipment(
        ShipmentDirection direction)
    {
        var shipment = NewShipment(direction);
        shipment.RecordReservation(
            "purchase-1",
            "provider-track-1",
            null,
            Now.AddMinutes(-2));
        shipment.RecordConfirmation(
            "courier-track-1",
            "booking",
            Now.AddMinutes(-1));
        return shipment;
    }

    private static ManagedShipment NewShipment(
        ShipmentDirection direction)
    {
        var draft = new ManagedShipmentDraft(
            "development-shipping",
            "seller-origin",
            "buyer-destination",
            "สินค้า",
            1_000,
            10,
            20,
            30,
            "THAIPOST",
            "EMS",
            "ไปรษณีย์ไทย EMS",
            4_500,
            0,
            0,
            null,
            "quote-1",
            Now.AddHours(1));
        return direction == ShipmentDirection.Outbound
            ? ManagedShipment.CreateOutbound(Guid.NewGuid(), draft, Now.AddMinutes(-3))
            : ManagedShipment.CreateReturn(Guid.NewGuid(), draft, Now.AddMinutes(-3));
    }
}
