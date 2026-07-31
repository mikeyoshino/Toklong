using Toklong.Domain.Common;

namespace Toklong.Domain.Accounts;

public enum AccountNameVerificationOperationStatus
{
    PendingProvider,
    ProviderVerified,
    ProviderRejected
}

public sealed class AccountNameVerificationOperation
{
    private AccountNameVerificationOperation() { }

    public AccountNameVerificationOperation(
        Guid id,
        Guid challengeId,
        string idempotencyKey,
        string submittedDigest,
        string providerVerificationKey,
        string phoneNumber,
        string providerChallengeId,
        DateTimeOffset createdAt)
    {
        if (id == Guid.Empty)
            throw new DomainException(
                "รหัสการยืนยันเปลี่ยนชื่อไม่ถูกต้อง");
        if (challengeId == Guid.Empty)
            throw new DomainException(
                "รหัสคำขอเปลี่ยนชื่อไม่ถูกต้อง");
        Id = id;
        ChallengeId = challengeId;
        IdempotencyKey = NormalizeGuidKey(
            idempotencyKey,
            "รหัสคำขอไม่ถูกต้อง");
        SubmittedDigest = NormalizeDigest(submittedDigest);
        ProviderVerificationKey = NormalizeGuidKey(
            providerVerificationKey,
            "รหัสอ้างอิงการยืนยันไม่ถูกต้อง");
        PhoneNumber = Required(phoneNumber, 20);
        ProviderChallengeId = Required(
            providerChallengeId,
            800);
        CreatedAt = createdAt;
        Status =
            AccountNameVerificationOperationStatus.PendingProvider;
    }

    public Guid Id { get; private set; }
    public Guid ChallengeId { get; private set; }
    public string IdempotencyKey { get; private set; } = "";
    public string SubmittedDigest { get; private set; } = "";
    public string ProviderVerificationKey { get; private set; } = "";
    public string PhoneNumber { get; private set; } = "";
    public string ProviderChallengeId { get; private set; } = "";
    public AccountNameVerificationOperationStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ProviderRequestedAt { get; private set; }
    public DateTimeOffset? ProviderCompletedAt { get; private set; }
    public DateTimeOffset? ProviderObservedAt { get; private set; }
    public long Version { get; private set; }

    public void EnsureExactReplay(string submittedDigest)
    {
        if (!string.Equals(
                SubmittedDigest,
                NormalizeDigest(submittedDigest),
                StringComparison.Ordinal))
            throw new DomainException(
                "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่");
    }

    public void RecordProviderOutcome(
        bool verified,
        DateTimeOffset requestedAt,
        DateTimeOffset completedAt,
        DateTimeOffset observedAt)
    {
        if (Status !=
            AccountNameVerificationOperationStatus.PendingProvider)
            return;
        if (requestedAt > completedAt ||
            completedAt > observedAt.AddMinutes(1))
            throw new DomainException(
                "หลักฐานการยืนยันจากผู้ให้บริการไม่ถูกต้อง");
        ProviderRequestedAt = requestedAt;
        ProviderCompletedAt = completedAt;
        ProviderObservedAt = observedAt;
        Status = verified
            ? AccountNameVerificationOperationStatus.ProviderVerified
            : AccountNameVerificationOperationStatus.ProviderRejected;
        Version++;
    }

    private static string NormalizeGuidKey(
        string value,
        string error)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new DomainException(error);
        return parsed.ToString("N");
    }

    private static string NormalizeDigest(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 64 ||
            clean.Any(character => !Uri.IsHexDigit(character)))
            throw new DomainException(
                "ข้อมูลอ้างอิงรหัสยืนยันไม่ถูกต้อง");
        return clean.ToLowerInvariant();
    }

    private static string Required(
        string value,
        int maximumLength)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length == 0 ||
            clean.Length > maximumLength)
            throw new DomainException(
                "ข้อมูลการยืนยันไม่ถูกต้อง");
        return clean;
    }
}
