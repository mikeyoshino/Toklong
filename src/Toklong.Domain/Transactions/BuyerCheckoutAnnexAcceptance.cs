using System.Security.Cryptography;
using System.Text;

namespace Toklong.Domain.Transactions;

/// <summary>
/// Immutable, buyer-facing checkout annex evidence. The payload deliberately
/// contains only the final commercial terms, never account or delivery data.
/// </summary>
public sealed class BuyerCheckoutAnnexAcceptance
{
    private BuyerCheckoutAnnexAcceptance() { }

    internal static BuyerCheckoutAnnexAcceptance Create(
        Guid transactionId,
        string canonicalPayloadJson,
        DateTimeOffset acceptedAt) => new()
    {
        TransactionId = transactionId,
        CanonicalPayloadJson = canonicalPayloadJson,
        PayloadHash = Hash(canonicalPayloadJson),
        AcceptedAt = acceptedAt
    };

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public string CanonicalPayloadJson { get; private set; } = "";
    public string PayloadHash { get; private set; } = "";
    public DateTimeOffset AcceptedAt { get; private set; }

    public bool HasValidPayloadHash() =>
        string.Equals(PayloadHash, Hash(CanonicalPayloadJson),
            StringComparison.Ordinal);

    private static string Hash(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
