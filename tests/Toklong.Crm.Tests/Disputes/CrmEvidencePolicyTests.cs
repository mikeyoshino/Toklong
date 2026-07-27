using Toklong.Crm.Disputes;
using Toklong.Domain.Transactions;

namespace Toklong.Crm.Tests.Disputes;

public sealed class CrmEvidencePolicyTests
{
    [Fact]
    public void Tampered_parcel_requires_evidence_from_both_parties()
    {
        var checklist = CrmEvidencePolicy.For(
            DisputeReason.EmptyOrTamperedParcel,
            FulfillmentType.PhysicalShipment,
            "งานอดิเรกและของใช้");

        Assert.Contains(
            checklist.BuyerEvidence,
            item => item.Contains("seal"));
        Assert.Contains(
            checklist.SellerEvidence,
            item => item.Contains("น้ำหนัก"));
    }

    [Fact]
    public void Digital_evidence_never_requests_reusable_credentials()
    {
        var checklist = CrmEvidencePolicy.For(
            DisputeReason.Other,
            FulfillmentType.DigitalHandoff,
            "สินค้าดิจิทัล");

        Assert.Contains(
            checklist.CategoryEvidence,
            item => item.Contains("ไม่ใช่รหัสผ่าน"));
    }
}
