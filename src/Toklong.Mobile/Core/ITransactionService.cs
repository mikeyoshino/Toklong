namespace Toklong.Mobile.Core;

public interface ITransactionService
{
    Task<BuyerCostPreview> GetBuyerCostPreviewAsync(
        long itemPriceSatang,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CarrierOption>> GetSupportedCarriersAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppTransaction>> GetTransactionsAsync(
        CancellationToken cancellationToken = default);

    Task<AppTransaction?> GetTransactionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    Task<BuyerParcelProtection> GetParcelProtectionAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<BuyerParcelProtection> PrepareParcelProtectionAsync(
        Guid transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<string> ChooseParcelProtectionAsync(
        Guid transactionId,
        bool addProtection,
        string? optionReference,
        long? disclosedCustomerPriceSatang,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<AgreementEvidenceFile> DownloadAgreementEvidenceAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<ShippingLabelFile> DownloadShippingLabelAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<ShippingLabelFile> DownloadReturnShippingLabelAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<AppTransaction> CreateBuyerOfferAsync(
        CreateBuyerOfferRequest request,
        CancellationToken cancellationToken = default);

    Task<AppTransaction> SubmitTrackingAsync(
        Guid transactionId,
        string carrierCode,
        string trackingNumber,
        CancellationToken cancellationToken = default);

    Task<AppTransaction> SubmitDigitalHandoffAsync(
        Guid transactionId,
        string statement,
        CancellationToken cancellationToken = default);

    Task<AppTransaction> ConfirmReceiptAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);

    Task<AppTransaction> OpenDisputeAsync(
        Guid transactionId,
        AppDisputeReason reason,
        string statement,
        CancellationToken cancellationToken = default);

    Task<DisputeEvidenceSummary> SubmitDisputeEvidenceAsync(
        Guid transactionId,
        AppDisputeEvidenceParty party,
        AppDisputeEvidenceType evidenceType,
        string description,
        DisputeEvidenceUpload file,
        string idempotencyKey,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<DisputeEvidenceSummary>>
        GetOwnDisputeEvidenceAsync(
            Guid transactionId,
            AppDisputeEvidenceParty party,
            CancellationToken cancellationToken = default) =>
        Task.FromResult<
            IReadOnlyList<DisputeEvidenceSummary>>([]);
}

public sealed record AgreementEvidenceFile(
    string FileName,
    byte[] Content);

public sealed record ShippingLabelFile(
    string FileName,
    byte[] Content);

public sealed record BuyerParcelProtection(
    bool RequiresChoice,
    bool AddOnAvailable,
    long IncludedCoverageLimitSatang,
    long? MaximumCoverageLimitSatang,
    long? CustomerPriceSatang,
    string? OptionReference,
    string TermsVersion,
    DateTimeOffset? ExpiresAt,
    string Election,
    bool BookingReady,
    bool ReconfirmationRequired);

public sealed record DisputeEvidenceUpload(
    string FileName,
    string ContentType,
    byte[] Content);

public sealed record DisputeEvidenceSummary(
    Guid Id,
    AppDisputeEvidenceParty Party,
    AppDisputeEvidenceType EvidenceType,
    string Description,
    string ContentType,
    long LengthBytes,
    string Sha256,
    DateTimeOffset SubmittedAt)
{
    public string DisplayText =>
        $"{EvidenceTypeLabel} · {Description}";

    public string EvidenceTypeLabel => EvidenceType switch
    {
        AppDisputeEvidenceType.Item => "ภาพสินค้า",
        AppDisputeEvidenceType.Packaging => "บรรจุภัณฑ์",
        AppDisputeEvidenceType.ShippingLabel => "ฉลากขนส่ง",
        AppDisputeEvidenceType.SerialOrIdentifier =>
            "Serial/จุดระบุสินค้า",
        AppDisputeEvidenceType.ReceiptOrProvenance =>
            "ใบเสร็จ/ที่มา",
        AppDisputeEvidenceType.HandoffRecord =>
            "หลักฐานส่งมอบ",
        _ => "หลักฐานอื่น"
    };
}

public enum AppDisputeEvidenceParty
{
    Buyer,
    Seller
}

public enum AppDisputeEvidenceType
{
    Item,
    Packaging,
    ShippingLabel,
    SerialOrIdentifier,
    ReceiptOrProvenance,
    HandoffRecord,
    Other
}

public sealed record CarrierOption(
    string Code,
    string DisplayName,
    string TrackingHint,
    string TrackingExample,
    string ValidationPattern,
    string ValidationMessage,
    int MaximumLength)
{
    public string Placeholder => $"เช่น {TrackingExample}";

    public string NormalizeTracking(string? value) =>
        new((value ?? "")
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .Take(MaximumLength)
            .ToArray());

    public bool IsValidTrackingNumber(string? value)
    {
        try
        {
            return System.Text.RegularExpressions.Regex.IsMatch(
                NormalizeTracking(value),
                ValidationPattern,
                System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}

public sealed record CreateBuyerOfferRequest(
    string SellerPhoneNumber,
    AppFulfillmentType FulfillmentType,
    AppCondition Condition,
    string ProductName,
    string AgreementDetails,
    string KnownDefects,
    long AmountSatang,
    string? LocalPhotoPath,
    bool UseSavedAddress,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    bool RememberAddress);

[System.Text.Json.Serialization.JsonConverter(
    typeof(System.Text.Json.Serialization.JsonStringEnumConverter<AppCondition>))]
public enum AppCondition
{
    New,
    UsedGood,
    UsedDefects
}

public enum AppDisputeReason
{
    NotReceived,
    WrongItem,
    NotAsDescribed,
    UndisclosedDamage,
    SuspectedCounterfeit,
    EmptyOrTamperedParcel,
    Other
}

public enum PaymentSheetOutcome
{
    Completed,
    Cancelled
}

public interface IStripePaymentSheetService
{
    Task<PaymentSheetOutcome> PresentAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default);
}
