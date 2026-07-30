namespace Toklong.Domain.Transactions;

public enum ParcelProtectionElectionStatus
{
    Pending,
    Accepted,
    Declined,
    NotApplicable,
    Unavailable,
    ReconfirmationRequired
}

public sealed record ParcelProtectionSelection(
    ParcelProtectionElectionStatus Election,
    long CustomerPriceSatang,
    long ProviderCostSatang,
    long ToklongServiceFeeSatang,
    long IncludedCoverageLimitSatang,
    long SelectedCoverageLimitSatang,
    string TermsVersion,
    string? ProviderOptionReference,
    DateTimeOffset QuotedAt,
    DateTimeOffset ExpiresAt);

public sealed record ParcelProtectionPreparedOffer(
    bool RequiresChoice,
    bool AddOnAvailable,
    long IncludedCoverageLimitSatang,
    long? MaximumCoverageLimitSatang,
    long? CustomerPriceSatang,
    string? OptionReference,
    string TermsVersion,
    DateTimeOffset? ExpiresAt,
    ParcelProtectionElectionStatus Election =
        ParcelProtectionElectionStatus.Pending);
