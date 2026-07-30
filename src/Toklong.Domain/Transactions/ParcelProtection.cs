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
