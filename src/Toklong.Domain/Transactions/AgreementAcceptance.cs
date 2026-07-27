namespace Toklong.Domain.Transactions;

public enum AgreementAcceptanceRole
{
    Buyer,
    Seller
}

public sealed class AgreementAcceptance
{
    private AgreementAcceptance() { }

    internal static AgreementAcceptance Create(
        Guid transactionId,
        AgreementAcceptanceRole role,
        Guid actorUserId,
        string verifiedPhoneNumber,
        string agreementCoreSnapshotHash,
        string termsVersion,
        string termsSnapshotHash,
        DateTimeOffset acceptedAt,
        string correlationId,
        string idempotencyKey) =>
        new()
        {
            TransactionId = transactionId,
            Role = role,
            ActorUserId = actorUserId,
            VerifiedPhoneNumber = verifiedPhoneNumber,
            AuthenticationMethod = "verified-phone-session",
            AgreementCoreSnapshotHash = agreementCoreSnapshotHash,
            TermsVersion = termsVersion,
            TermsSnapshotHash = termsSnapshotHash,
            AcceptedAt = acceptedAt,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey
        };

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public AgreementAcceptanceRole Role { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string VerifiedPhoneNumber { get; private set; } = "";
    public string AuthenticationMethod { get; private set; } = "";
    public string AgreementCoreSnapshotHash { get; private set; } = "";
    public string TermsVersion { get; private set; } = "";
    public string TermsSnapshotHash { get; private set; } = "";
    public DateTimeOffset AcceptedAt { get; private set; }
    public string CorrelationId { get; private set; } = "";
    public string IdempotencyKey { get; private set; } = "";
}
