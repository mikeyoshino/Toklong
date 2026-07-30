using Toklong.Domain.Common;

namespace Toklong.Domain.Transactions;

public enum ParcelProtectionChangeStatus
{
    AwaitingCancellation,
    AwaitingRebooking,
    Completed
}

public sealed class ParcelProtectionChangeRequest
{
    private ParcelProtectionChangeRequest() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid PreviousManagedShipmentId { get; private set; }
    public ParcelProtectionChangeStatus Status { get; private set; }
    public ParcelProtectionElectionStatus DesiredElection { get; private set; }
    public long DesiredCustomerPriceSatang { get; private set; }
    public long DesiredProviderCostSatang { get; private set; }
    public long DesiredServiceFeeSatang { get; private set; }
    public long DesiredIncludedCoverageSatang { get; private set; }
    public long DesiredSelectedCoverageSatang { get; private set; }
    public string DesiredTermsVersion { get; private set; } = "";
    public string? DesiredOptionReference { get; private set; }
    public string? DesiredInsuranceCode { get; private set; }
    public DateTimeOffset DesiredQuotedAt { get; private set; }
    public DateTimeOffset DesiredExpiresAt { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    public static ParcelProtectionChangeRequest Create(
        Guid transactionId,
        Guid previousManagedShipmentId,
        ParcelProtectionSelection desiredSelection,
        string idempotencyKey,
        DateTimeOffset now,
        string? desiredInsuranceCode = null)
    {
        ArgumentNullException.ThrowIfNull(desiredSelection);
        if (transactionId == Guid.Empty || previousManagedShipmentId == Guid.Empty)
            throw new DomainException("ข้อมูลการเปลี่ยนความคุ้มครองพัสดุไม่ถูกต้อง");
        if (desiredSelection.QuotedAt > now || desiredSelection.ExpiresAt <= now ||
            string.IsNullOrWhiteSpace(desiredSelection.TermsVersion) ||
            desiredSelection.Election is not (
                ParcelProtectionElectionStatus.Accepted or
                ParcelProtectionElectionStatus.Declined or
                ParcelProtectionElectionStatus.Unavailable or
                ParcelProtectionElectionStatus.NotApplicable))
            throw new DomainException("ตัวเลือกความคุ้มครองพัสดุไม่ถูกต้อง");
        if (desiredSelection.Election == ParcelProtectionElectionStatus.Accepted &&
            (desiredSelection.CustomerPriceSatang <= 0 ||
             desiredSelection.ToklongServiceFeeSatang !=
                 SaleTransaction.ParcelProtectionServiceFeeAmountSatang ||
             desiredSelection.CustomerPriceSatang != checked(
                 desiredSelection.ProviderCostSatang +
                 desiredSelection.ToklongServiceFeeSatang) ||
             string.IsNullOrWhiteSpace(desiredSelection.ProviderOptionReference)))
            throw new DomainException("ราคาความคุ้มครองพัสดุไม่ถูกต้อง");
        if (desiredSelection.Election != ParcelProtectionElectionStatus.Accepted &&
            (desiredSelection.CustomerPriceSatang != 0 ||
             desiredSelection.ProviderCostSatang != 0 ||
             desiredSelection.ToklongServiceFeeSatang != 0 ||
             !string.IsNullOrWhiteSpace(desiredSelection.ProviderOptionReference)))
            throw new DomainException("ตัวเลือกความคุ้มครองพัสดุนี้ต้องไม่มีค่าใช้จ่าย");

        return new ParcelProtectionChangeRequest
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            PreviousManagedShipmentId = previousManagedShipmentId,
            Status = ParcelProtectionChangeStatus.AwaitingCancellation,
            DesiredElection = desiredSelection.Election,
            DesiredCustomerPriceSatang = desiredSelection.CustomerPriceSatang,
            DesiredProviderCostSatang = desiredSelection.ProviderCostSatang,
            DesiredServiceFeeSatang = desiredSelection.ToklongServiceFeeSatang,
            DesiredIncludedCoverageSatang = desiredSelection.IncludedCoverageLimitSatang,
            DesiredSelectedCoverageSatang = desiredSelection.SelectedCoverageLimitSatang,
            DesiredTermsVersion = desiredSelection.TermsVersion.Trim(),
            DesiredOptionReference = Optional(desiredSelection.ProviderOptionReference, 160),
            DesiredInsuranceCode = Optional(desiredInsuranceCode, 80),
            DesiredQuotedAt = desiredSelection.QuotedAt,
            DesiredExpiresAt = desiredSelection.ExpiresAt,
            IdempotencyKey = Required(idempotencyKey, 80),
            CreatedAt = now
        };
    }

    public ParcelProtectionSelection DesiredSelection() => new(
        DesiredElection, DesiredCustomerPriceSatang, DesiredProviderCostSatang,
        DesiredServiceFeeSatang, DesiredIncludedCoverageSatang,
        DesiredSelectedCoverageSatang, DesiredTermsVersion,
        DesiredOptionReference, DesiredQuotedAt, DesiredExpiresAt);

    public void MarkAwaitingRebooking()
    {
        if (Status != ParcelProtectionChangeStatus.AwaitingCancellation)
            throw new DomainException("สถานะการเปลี่ยนความคุ้มครองพัสดุไม่ถูกต้อง");
        Status = ParcelProtectionChangeStatus.AwaitingRebooking;
    }

    public void Complete(DateTimeOffset now)
    {
        if (Status != ParcelProtectionChangeStatus.AwaitingRebooking)
            throw new DomainException("สถานะการเปลี่ยนความคุ้มครองพัสดุไม่ถูกต้อง");
        Status = ParcelProtectionChangeStatus.Completed;
        CompletedAt = now;
    }

    private static string Required(string? value, int maximumLength)
    {
        var clean = value?.Trim() ?? "";
        if (clean.Length == 0 || clean.Length > maximumLength)
            throw new DomainException("รหัสป้องกันการทำซ้ำไม่ถูกต้อง");
        return clean;
    }

    private static string? Optional(string? value, int maximumLength)
    {
        var clean = value?.Trim();
        if (string.IsNullOrWhiteSpace(clean)) return null;
        if (clean.Length > maximumLength)
            throw new DomainException("ข้อมูลความคุ้มครองพัสดุไม่ถูกต้อง");
        return clean;
    }
}
