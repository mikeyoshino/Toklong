namespace Toklong.Mobile.Core;

public sealed record MobilePayoutAccount(
    Guid Id,
    string BankCode,
    string AccountName,
    string MaskedNumber,
    bool IsDefault);

public sealed record MobileSavedShippingOrigin(
    string DisplayText,
    int ProvinceId,
    string ProvinceName,
    int DistrictId,
    string DistrictName,
    int SubdistrictId,
    string SubdistrictName,
    string PostalCode);

public sealed record MobileShippingQuote(
    string Provider,
    string QuoteReference,
    string CarrierCode,
    string ServiceCode,
    string ServiceName,
    long FeeSatang,
    DateTimeOffset ExpiresAt)
{
    public string FeeText =>
        MoneyFormatter.Format(FeeSatang, "THB");

    public string DisplayText =>
        $"{ServiceName} · {FeeText}";
}

public sealed record SellerShippingQuoteRequest(
    bool UseSavedOrigin,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters);

public sealed record SellerShippingSelection(
    bool UseSavedOrigin,
    string? AddressLine,
    int? ProvinceId,
    int? DistrictId,
    int? SubdistrictId,
    bool RememberOrigin,
    int WeightGrams,
    int WidthCentimeters,
    int LengthCentimeters,
    int HeightCentimeters,
    string QuoteReference,
    long DisclosedShippingFeeSatang);

public sealed record SellerOfferInvitation(
    AppTransaction Transaction,
    long SellerExpectedNetSatang,
    IReadOnlyList<MobilePayoutAccount> PayoutAccounts,
    MobileSavedShippingOrigin? SavedShippingOrigin);

public interface ISellerOfferService
{
    Task<IReadOnlyList<MobilePayoutAccount>> GetPayoutAccountsAsync(
        CancellationToken cancellationToken = default);

    Task<SellerOfferInvitation> GetAsync(
        string publicToken,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MobileShippingQuote>> GetShippingQuotesAsync(
        string publicToken,
        SellerShippingQuoteRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MobilePayoutAccount>> SavePayoutAccountAsync(
        Guid? accountId,
        string bankCode,
        string accountName,
        string accountNumber,
        CancellationToken cancellationToken = default);

    Task<AppTransaction> AcceptAsync(
        string publicToken,
        Guid payoutAccountId,
        bool transferRightsAttested,
        bool sellerAcceptedTerms,
        SellerShippingSelection? shipping,
        CancellationToken cancellationToken = default);

    Task<AppTransaction> DeclineAsync(
        string publicToken,
        CancellationToken cancellationToken = default);
}

public interface IPendingSellerOfferStore
{
    string? PendingToken { get; }
    Guid? PendingTransactionId { get; }
    void Save(string publicToken);
    void SaveTransaction(Guid transactionId);
    string? Take();
    Guid? TakeTransaction();
}
