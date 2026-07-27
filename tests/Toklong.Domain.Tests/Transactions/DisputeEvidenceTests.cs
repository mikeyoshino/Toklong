using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Domain.Tests.Transactions;

public sealed class DisputeEvidenceTests
{
    private static readonly DateTimeOffset Start =
        new(2026, 7, 26, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Buyer_and_seller_can_submit_distinct_evidence_with_same_client_key()
    {
        var transaction = DisputedTransaction();
        var buyerEvidence = Record(
            transaction,
            DisputeEvidenceParty.Buyer,
            transaction.BuyerId!.Value,
            "retry-key");
        var sellerEvidence = Record(
            transaction,
            DisputeEvidenceParty.Seller,
            transaction.SellerId!.Value,
            "retry-key");

        Assert.NotEqual(buyerEvidence.Id, sellerEvidence.Id);
        Assert.Equal(2, transaction.DisputeEvidence.Count);
        Assert.Equal(
            2,
            transaction.AuditEvents.Count(item =>
                item.Name == "dispute.evidence_submitted"));
    }

    [Fact]
    public void Same_party_retry_returns_original_without_duplicate_audit()
    {
        var transaction = DisputedTransaction();
        var original = Record(
            transaction,
            DisputeEvidenceParty.Buyer,
            transaction.BuyerId!.Value,
            "retry-key");
        var replay = Record(
            transaction,
            DisputeEvidenceParty.Buyer,
            transaction.BuyerId!.Value,
            "retry-key");

        Assert.Same(original, replay);
        Assert.Single(transaction.DisputeEvidence);
        Assert.Single(
            transaction.AuditEvents,
            item => item.Name == "dispute.evidence_submitted");
    }

    [Fact]
    public void Reusable_credentials_are_rejected_from_evidence_description()
    {
        var transaction = DisputedTransaction();

        var exception = Assert.Throws<DomainException>(() =>
            Record(
                transaction,
                DisputeEvidenceParty.Buyer,
                transaction.BuyerId!.Value,
                "secret-key",
                "private key: do-not-store"));

        Assert.Contains("ข้อมูลลับ", exception.Message);
        Assert.Empty(transaction.DisputeEvidence);
    }

    [Fact]
    public void Evidence_is_rejected_before_a_dispute_is_open()
    {
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานได้ปกติ",
            ConditionCode.UsedGood,
            "",
            null,
            450_000,
            "mvp-th-2026-07",
            Start,
            new TransactionTransitionService());

        Assert.Throws<DomainException>(() =>
            Record(
                transaction,
                DisputeEvidenceParty.Buyer,
                transaction.BuyerId!.Value,
                "not-disputed"));
        Assert.Empty(transaction.DisputeEvidence);
    }

    [Fact]
    public void Evidence_request_notifies_each_target_once_with_exact_deadline()
    {
        var transaction = DisputedTransaction();
        var requestId = Guid.NewGuid();
        var dueAt = Start.AddHours(50);

        Assert.True(transaction.RequestDisputeEvidence(
            requestId,
            DisputeEvidenceParty.Buyer,
            Guid.NewGuid(),
            "รูปฉลากขนส่งและบรรจุภัณฑ์",
            dueAt,
            Start.AddHours(2)));
        Assert.False(transaction.RequestDisputeEvidence(
            requestId,
            DisputeEvidenceParty.Buyer,
            Guid.NewGuid(),
            "รูปฉลากขนส่งและบรรจุภัณฑ์",
            dueAt,
            Start.AddHours(2)));
        Assert.True(transaction.RequestDisputeEvidence(
            requestId,
            DisputeEvidenceParty.Seller,
            Guid.NewGuid(),
            "รูปฉลากขนส่งและบรรจุภัณฑ์",
            dueAt,
            Start.AddHours(2)));

        var messages = transaction.Notifications
            .Where(item =>
                item.Template ==
                "dispute_evidence_requested")
            .ToList();
        Assert.Equal(2, messages.Count);
        Assert.Contains(
            messages,
            item => item.Audience == "buyer" &&
                    item.ActionDeadlineAt == dueAt);
        Assert.Contains(
            messages,
            item => item.Audience == "seller" &&
                    item.ActionDeadlineAt == dueAt);
        Assert.Equal(
            2,
            transaction.AuditEvents.Count(item =>
                item.Name ==
                "dispute.evidence_requested"));
    }

    private static DisputeEvidence Record(
        SaleTransaction transaction,
        DisputeEvidenceParty party,
        Guid submittedById,
        string idempotencyKey,
        string description = "ภาพสภาพสินค้าหลังเปิดกล่อง") =>
        transaction.RecordDisputeEvidence(
            Guid.NewGuid(),
            party,
            submittedById,
            DisputeEvidenceType.Item,
            description,
            $"evidence:{Guid.NewGuid():N}.bin",
            "image/jpeg",
            512,
            new string('a', 64),
            idempotencyKey,
            Start.AddHours(3));

    private static SaleTransaction DisputedTransaction()
    {
        var transitions = new TransactionTransitionService();
        var transaction = TestTransactionFactory.CreateBuyerOffer(
            Guid.NewGuid(),
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "+66822222222",
            FulfillmentType.PhysicalShipment,
            "กล้องพร้อมเลนส์",
            "ใช้งานได้ปกติ",
            ConditionCode.UsedGood,
            "",
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
            shipping: TestTransactionFactory.ShippingQuote(
                Start.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 กรุงเทพฯ",
            Start.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "payment-evidence",
            Start.AddMinutes(3),
            transitions);
        transaction.SubmitTracking(
            transaction.SellerAccessToken,
            "FLASH",
            "TH12345678",
            Start.AddMinutes(4),
            transitions);
        transaction.RecordCarrierEvent(
            "delivery-evidence",
            "delivered",
            Start.AddHours(1),
            Start.AddHours(1),
            transitions);
        transaction.OpenDispute(
            transaction.BuyerAccessToken!,
            DisputeReason.NotAsDescribed,
            "สินค้าไม่ตรงตามรายละเอียด",
            Start.AddHours(2),
            transitions);
        return transaction;
    }
}
