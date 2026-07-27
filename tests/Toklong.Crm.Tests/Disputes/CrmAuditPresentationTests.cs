using Toklong.Crm.Disputes;
using Toklong.Domain.Transactions;

namespace Toklong.Crm.Tests.Disputes;

public sealed class CrmAuditPresentationTests
{
    [Fact]
    public void DisputeOpened_IsPresentedInPlainThai()
    {
        var display = CrmAuditPresentation.For(
            Audit(
                "dispute.opened",
                TransactionState.DeliveredDisputeWindow,
                TransactionState.Disputed,
                ActorRole.Buyer));

        Assert.Equal("ผู้ซื้อแจ้งปัญหา", display.Title);
        Assert.Contains("หยุดการจ่ายเงิน", display.Description);
        Assert.Equal("ผู้ซื้อ", display.ActorLabel);
        Assert.Equal(
            "ส่งถึงแล้ว อยู่ในช่วงตรวจสินค้า",
            display.FromStateLabel);
        Assert.Equal("มีข้อโต้แย้ง", display.ToStateLabel);
        Assert.Equal("danger", display.Tone);
    }

    [Fact]
    public void UnknownEvent_UsesUnderstandableFallback()
    {
        var display = CrmAuditPresentation.For(
            Audit(
                "provider.future_event",
                TransactionState.PaymentPending,
                TransactionState.PaymentPending,
                ActorRole.System));

        Assert.Equal(
            "ระบบบันทึกการเปลี่ยนแปลง",
            display.Title);
        Assert.DoesNotContain(
            "provider.future_event",
            display.Title);
        Assert.Equal("ระบบ TOKLONG", display.ActorLabel);
        Assert.Equal("neutral", display.Tone);
    }

    [Fact]
    public void CarrierInTransit_ExplainsTheShipmentProgress()
    {
        var display = CrmAuditPresentation.For(
            Audit(
                "carrier.in_transit",
                TransactionState.TrackingSubmitted,
                TransactionState.InTransit,
                ActorRole.CarrierProvider));

        Assert.Equal(
            "ขนส่งรับพัสดุเข้าระบบแล้ว",
            display.Title);
        Assert.Equal(
            "ผู้ให้บริการขนส่ง",
            display.ActorLabel);
        Assert.Equal("อยู่ระหว่างขนส่ง", display.ToStateLabel);
        Assert.Equal("info", display.Tone);
    }

    [Fact]
    public void TimelyAcceptanceRecovery_ExplainsWhyAutomaticRefundStopped()
    {
        var display = CrmAuditPresentation.For(
            Audit(
                "shipment.timely_acceptance_recovered",
                TransactionState.RefundPending,
                TransactionState.TrackingUnverified,
                ActorRole.Reconciliation));

        Assert.Equal(
            "ยืนยันว่าผู้ขายส่งพัสดุทันเวลา",
            display.Title);
        Assert.Contains(
            "หยุดการคืนเงินอัตโนมัติ",
            display.Description);
        Assert.Equal("warning", display.Tone);
    }

    [Theory]
    [InlineData(
        "refund.action_required",
        "ผู้ซื้อต้องยืนยันข้อมูลรับเงินคืน",
        "warning")]
    [InlineData(
        "refund.processing",
        "ผู้ให้บริการกำลังคืนเงิน",
        "info")]
    public void RefundProgress_IsPresentedWithoutTechnicalLanguage(
        string eventName,
        string title,
        string tone)
    {
        var display = CrmAuditPresentation.For(
            Audit(
                eventName,
                TransactionState.RefundPending,
                TransactionState.RefundPending,
                ActorRole.PaymentProvider));

        Assert.Equal(title, display.Title);
        Assert.Equal(tone, display.Tone);
        Assert.DoesNotContain(
            "requires_action",
            display.Description);
    }

    [Fact]
    public void ActorReference_IsStableAndDoesNotExposeRawIdentifier()
    {
        const string actorId =
            "Bearer secret-access-token-value";

        var first = CrmAuditReference.FromActorId(actorId);
        var second = CrmAuditReference.FromActorId(actorId);
        var different = CrmAuditReference.FromActorId(
            "another-actor");

        Assert.Equal(first, second);
        Assert.StartsWith("ref-", first);
        Assert.Equal(16, first.Length);
        Assert.DoesNotContain(actorId, first);
        Assert.NotEqual(first, different);
    }

    private static CrmCoreAuditView Audit(
        string name,
        TransactionState fromState,
        TransactionState toState,
        ActorRole actorRole) =>
        new(
            name,
            fromState,
            toState,
            actorRole,
            "ref-123456789abc",
            "correlation",
            "idempotency",
            "{}",
            DateTimeOffset.Parse(
                "2026-07-26T08:00:00+00:00"));
}
