using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public sealed class ProviderShippingAdjustment
{
    private ProviderShippingAdjustment() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid ManagedShipmentId { get; private set; }
    public string Provider { get; private set; } = "";
    public string ProviderReference { get; private set; } = "";
    public long AmountSatang { get; private set; }
    public string Currency { get; private set; } = "THB";
    public DateTimeOffset ProviderOccurredAt { get; private set; }
    public string CrmCaseReference { get; private set; } = "";
    public string ReasonCode { get; private set; } = "";
    public DateTimeOffset RecordedAt { get; private set; }

    public static ProviderShippingAdjustment Create(
        Guid transactionId,
        Guid managedShipmentId,
        string provider,
        string providerReference,
        long amountSatang,
        string currency,
        DateTimeOffset providerOccurredAt,
        string crmCaseReference,
        string reasonCode,
        DateTimeOffset recordedAt)
    {
        if (transactionId == Guid.Empty ||
            managedShipmentId == Guid.Empty)
            throw new DomainException("ข้อมูลรายการจัดส่งไม่ครบ");
        if (amountSatang <= 0)
            throw new DomainException(
                "ยอดปรับค่าจัดส่งต้องมากกว่าศูนย์");
        if (!string.Equals(
                currency?.Trim(),
                "THB",
                StringComparison.Ordinal))
            throw new DomainException(
                "รองรับยอดปรับค่าจัดส่งสกุล THB เท่านั้น");
        if (providerOccurredAt == default)
            throw new DomainException(
                "ไม่พบเวลาจากผู้ให้บริการ");

        return new ProviderShippingAdjustment
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            ManagedShipmentId = managedShipmentId,
            Provider = Required(provider, "ผู้ให้บริการ", 80),
            ProviderReference = Required(
                providerReference,
                "เลขอ้างอิงผู้ให้บริการ",
                160),
            AmountSatang = amountSatang,
            ProviderOccurredAt = providerOccurredAt,
            CrmCaseReference = Required(
                crmCaseReference,
                "เลขเคส CRM",
                160),
            ReasonCode = Required(reasonCode, "เหตุผล", 100),
            RecordedAt = recordedAt
        };
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
