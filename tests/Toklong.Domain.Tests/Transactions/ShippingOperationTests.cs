using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class ShippingOperationTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 13, 0, 0, TimeSpan.Zero);
    private static readonly string Fingerprint =
        new('a', 64);

    [Fact]
    public void Live_lease_cannot_be_claimed_by_a_second_worker()
    {
        var operation = ShippingOperation.Queue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShippingOperationType.BookOutbound,
            "book-outbound:test",
            Fingerprint,
            Now);

        operation.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(5));

        Assert.Throws<DomainException>(() =>
            operation.Claim(
                "worker-b",
                Now.AddMinutes(1),
                TimeSpan.FromMinutes(5)));
        Assert.Equal(
            ShippingOperationStatus.Processing,
            operation.Status);
        Assert.Equal("worker-a", operation.LeaseOwner);
        Assert.Equal(1, operation.AttemptCount);
    }

    [Fact]
    public void Expired_lease_can_be_reclaimed()
    {
        var operation = ShippingOperation.Queue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShippingOperationType.ConfirmOutbound,
            "confirm-outbound:test",
            Fingerprint,
            Now);
        operation.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(5));

        operation.Claim(
            "worker-b",
            Now.AddMinutes(6),
            TimeSpan.FromMinutes(5));

        Assert.Equal("worker-b", operation.LeaseOwner);
        Assert.Equal(2, operation.AttemptCount);
    }

    [Fact]
    public void Unknown_outcome_cannot_be_retried_without_proven_provider_safety()
    {
        var operation = ShippingOperation.Queue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShippingOperationType.BookOutbound,
            "book-outbound:unknown",
            Fingerprint,
            Now);
        operation.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(5));
        operation.MarkOutcomeUnknown(
            "worker-a",
            "provider-timeout",
            Now.AddSeconds(20));

        Assert.Throws<DomainException>(() =>
            operation.ScheduleRetry(
                "reconciliation",
                Now.AddMinutes(5),
                "retry-requested",
                providerReplayProvenSafe: false,
                Now.AddMinutes(1)));
        Assert.Equal(
            ShippingOperationStatus.OutcomeUnknown,
            operation.Status);
        Assert.Null(operation.LeaseOwner);
    }

    [Fact]
    public void Matching_lease_owner_can_complete_operation_once()
    {
        var operation = ShippingOperation.Queue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShippingOperationType.CancelOutbound,
            "cancel-outbound:test",
            Fingerprint,
            Now);
        operation.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(5));

        operation.Succeed(
            "worker-a",
            "purchase-123",
            "tracking-123",
            Now.AddSeconds(2));

        Assert.Equal(
            ShippingOperationStatus.Succeeded,
            operation.Status);
        Assert.Equal(
            "purchase-123",
            operation.ProviderPurchaseReference);
        Assert.Equal(
            "tracking-123",
            operation.ProviderTrackingReference);
        Assert.NotNull(operation.CompletedAt);
        Assert.Null(operation.LeaseOwner);
        Assert.Throws<DomainException>(() =>
            operation.Succeed(
                "worker-a",
                "purchase-123",
                "tracking-123",
                Now.AddSeconds(3)));
    }
}
