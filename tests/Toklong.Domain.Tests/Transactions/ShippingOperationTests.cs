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
    public void Review_operation_requires_explicit_replay_proof_before_retry()
    {
        var operation = ShippingOperation.Queue(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ShippingOperationType.BookReturn,
            "book-return:review",
            Fingerprint,
            Now);
        operation.Claim(
            "worker-a",
            Now,
            TimeSpan.FromMinutes(5));
        operation.SendToReview(
            "worker-a",
            "provider-result-mismatch",
            Now.AddSeconds(20));

        Assert.Throws<DomainException>(() =>
            operation.ScheduleRetry(
                "crm-user",
                Now.AddMinutes(5),
                "manual-review",
                providerReplayProvenSafe: false,
                Now.AddMinutes(1)));

        operation.ScheduleRetry(
            "crm-user",
            Now.AddMinutes(5),
            "authorized-provider-reconciliation",
            providerReplayProvenSafe: true,
            Now.AddMinutes(1));

        Assert.Equal(
            ShippingOperationStatus.RetryScheduled,
            operation.Status);
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

    [Fact]
    public void Provider_adjustment_requires_positive_thb_and_crm_reference()
    {
        var adjustment = ProviderShippingAdjustment.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "shippop",
            "adjustment-001",
            1_500,
            "THB",
            Now,
            "CRM-SHIP-001",
            "carrier-surcharge",
            Now.AddMinutes(1));

        Assert.Equal(1_500, adjustment.AmountSatang);
        Assert.Equal("THB", adjustment.Currency);
        Assert.True(adjustment.IsOpen);
        adjustment.Resolve(
            ActorRole.Reconciliation,
            "crm-user",
            "absorbed-by-platform",
            Now.AddMinutes(2));
        Assert.False(adjustment.IsOpen);
        Assert.Equal(
            "absorbed-by-platform",
            adjustment.ResolutionCode);
        Assert.Throws<DomainException>(() =>
            ProviderShippingAdjustment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "shippop",
                "adjustment-002",
                0,
                "THB",
                Now,
                "CRM-SHIP-002",
                "carrier-surcharge",
                Now));
        Assert.Throws<DomainException>(() =>
            ProviderShippingAdjustment.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "shippop",
                "adjustment-003",
                100,
                "USD",
                Now,
                "CRM-SHIP-003",
                "carrier-surcharge",
                Now));
    }

    [Fact]
    public void Insurance_case_resolution_only_records_authorized_provider_result()
    {
        var insuranceCase = ShippingInsuranceCase.Open(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "shippop",
            "provider-case-001",
            "parcel-damaged",
            120_000,
            120_000,
            "THB",
            "CRM-SHIP-004",
            "crm-user-1",
            Now);

        Assert.Throws<DomainException>(() =>
            insuranceCase.Resolve(
                ActorRole.Seller,
                "seller-1",
                "rejected",
                "provider-resolution-1",
                Now.AddHours(1)));

        insuranceCase.Resolve(
            ActorRole.Reconciliation,
            "crm-user-2",
            "approved",
            "provider-resolution-1",
            Now.AddHours(1));

        Assert.Equal(
            ShippingInsuranceCaseStatus.Resolved,
            insuranceCase.Status);
        Assert.Equal("approved", insuranceCase.ProviderResultCode);
        Assert.Null(insuranceCase.TransactionOutcome);
    }

    [Fact]
    public void Aggregate_owns_one_outbound_and_at_most_one_return()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
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
        var outbound = ManagedShipment.CreateOutbound(
            transaction.Id,
            ShipmentDraft(),
            Now);
        var outboundOperation = ShippingOperation.Queue(
            transaction.Id,
            outbound.Id,
            ShippingOperationType.BookOutbound,
            $"book-outbound:{transaction.Id:N}:test",
            Fingerprint,
            Now);

        transaction.QueueManagedShipment(
            outbound,
            outboundOperation,
            ActorRole.System,
            "shipping-orchestrator",
            Now);

        var duplicateOutbound = ManagedShipment.CreateOutbound(
            transaction.Id,
            ShipmentDraft(),
            Now);
        Assert.Throws<DomainException>(() =>
            transaction.QueueManagedShipment(
                duplicateOutbound,
                ShippingOperation.Queue(
                    transaction.Id,
                    duplicateOutbound.Id,
                    ShippingOperationType.BookOutbound,
                    $"book-outbound:{transaction.Id:N}:duplicate",
                    Fingerprint,
                    Now),
                ActorRole.System,
                "shipping-orchestrator",
                Now));

        Assert.Single(transaction.ManagedShipments);
        Assert.Single(transaction.ShippingOperations);
        Assert.Single(
            transaction.AuditEvents,
            audit => audit.Name == "shipping.operation_queued");
    }

    private static ManagedShipmentDraft ShipmentDraft() =>
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
