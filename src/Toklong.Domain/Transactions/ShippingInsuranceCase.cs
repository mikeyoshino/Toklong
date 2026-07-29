using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public enum ShippingInsuranceCaseStatus
{
    Open,
    Resolved
}

public sealed class ShippingInsuranceCase
{
    private ShippingInsuranceCase() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid ManagedShipmentId { get; private set; }
    public string Provider { get; private set; } = "";
    public string ProviderCaseReference { get; private set; } = "";
    public string ReasonCode { get; private set; } = "";
    public long DeclaredValueSatang { get; private set; }
    public long ClaimedAmountSatang { get; private set; }
    public string Currency { get; private set; } = "THB";
    public string CrmCaseReference { get; private set; } = "";
    public string OpenedBy { get; private set; } = "";
    public DateTimeOffset OpenedAt { get; private set; }
    public ShippingInsuranceCaseStatus Status { get; private set; }
    public string? ResolvedBy { get; private set; }
    public string? ProviderResultCode { get; private set; }
    public string? ProviderResolutionReference { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? TransactionOutcome => null;
    public long Version { get; private set; }

    public static ShippingInsuranceCase Open(
        Guid transactionId,
        Guid managedShipmentId,
        string provider,
        string providerCaseReference,
        string reasonCode,
        long declaredValueSatang,
        long claimedAmountSatang,
        string currency,
        string crmCaseReference,
        string openedBy,
        DateTimeOffset openedAt)
    {
        if (transactionId == Guid.Empty ||
            managedShipmentId == Guid.Empty)
            throw new DomainException("ข้อมูลรายการจัดส่งไม่ครบ");
        if (declaredValueSatang <= 0 ||
            claimedAmountSatang <= 0 ||
            claimedAmountSatang > declaredValueSatang)
            throw new DomainException(
                "ยอดเคลมประกันพัสดุไม่ถูกต้อง");
        if (!string.Equals(
                currency?.Trim(),
                "THB",
                StringComparison.Ordinal))
            throw new DomainException(
                "รองรับประกันพัสดุสกุล THB เท่านั้น");

        return new ShippingInsuranceCase
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            ManagedShipmentId = managedShipmentId,
            Provider = Required(provider, "ผู้ให้บริการ", 80),
            ProviderCaseReference = Required(
                providerCaseReference,
                "เลขเคสผู้ให้บริการ",
                160),
            ReasonCode = Required(reasonCode, "เหตุผล", 100),
            DeclaredValueSatang = declaredValueSatang,
            ClaimedAmountSatang = claimedAmountSatang,
            CrmCaseReference = Required(
                crmCaseReference,
                "เลขเคส CRM",
                160),
            OpenedBy = Required(openedBy, "ผู้เปิดเคส", 120),
            OpenedAt = openedAt,
            Status = ShippingInsuranceCaseStatus.Open
        };
    }

    public void Resolve(
        ActorRole actorRole,
        string actorId,
        string providerResultCode,
        string providerResolutionReference,
        DateTimeOffset resolvedAt)
    {
        if (actorRole is not (
                ActorRole.Reconciliation or
                ActorRole.System))
            throw new DomainException(
                "ไม่มีสิทธิ์ปิดเคสประกันพัสดุ");
        if (Status != ShippingInsuranceCaseStatus.Open)
            throw new DomainException(
                "เคสประกันพัสดุปิดแล้ว");

        ResolvedBy = Required(actorId, "ผู้ปิดเคส", 120);
        ProviderResultCode = Required(
            providerResultCode,
            "ผลจากผู้ให้บริการ",
            100);
        ProviderResolutionReference = Required(
            providerResolutionReference,
            "เลขอ้างอิงผลจากผู้ให้บริการ",
            160);
        ResolvedAt = resolvedAt;
        Status = ShippingInsuranceCaseStatus.Resolved;
        Version++;
    }

    private static string Required(
        string? value,
        string label,
        int maximumLength)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0 ||
            clean.Length > maximumLength)
            throw new DomainException($"{label}ไม่ถูกต้อง");
        return clean;
    }
}
