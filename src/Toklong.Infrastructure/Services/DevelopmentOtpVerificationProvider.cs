using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;

namespace Toklong.Infrastructure.Services;

public sealed class DevelopmentOtpVerificationProvider
    : IOtpVerificationProvider
{
    private static readonly ConcurrentDictionary<string, Entry> Challenges = new();
    private static readonly ConcurrentDictionary<
        (string Phone, OtpPurpose Purpose),
        DateTimeOffset> LastRequests = new();
    private static readonly ConcurrentDictionary<
        (string RequestKey, OtpPurpose Purpose),
        string> Requests = new();
    private static readonly ConcurrentDictionary<
        (string RequestKey, OtpPurpose Purpose),
        Lazy<Task<VerificationEntry>>> Verifications = new();
    private static readonly TimeSpan AuthenticationLifetime =
        TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AccountNameChangeLifetime =
        TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);
    private readonly IHostEnvironment _environment;
    private readonly IClock _clock;

    public DevelopmentOtpVerificationProvider(
        IHostEnvironment environment)
        : this(environment, new SystemClock())
    {
    }

    public DevelopmentOtpVerificationProvider(
        IHostEnvironment environment,
        IClock clock)
    {
        _environment = environment;
        _clock = clock;
    }

    public OtpProviderCapabilities Capabilities { get; } =
        new(true, TimeSpan.FromMinutes(10), true)
        {
            SupportsVerificationLookup = true
        };

    public Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        OtpPurpose purpose,
        string providerRequestKey,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่าผู้ให้บริการ OTP สำหรับ production");

        var normalized = NormalizeThaiPhone(phoneNumber);
        providerRequestKey = ValidProviderRequestKey(
            providerRequestKey);
        if (Requests.TryGetValue(
                (providerRequestKey, purpose),
                out var existingChallengeId) &&
            Challenges.TryGetValue(
                existingChallengeId,
                out var existing))
            return Task.FromResult(ToChallenge(
                existingChallengeId,
                existing));
        var now = _clock.UtcNow;
        var requestKey = (normalized, purpose);
        if (LastRequests.TryGetValue(requestKey, out var last) &&
            now - last < ResendDelay)
        {
            var retryAfter = ResendDelay - (now - last);
            var seconds = Math.Max(
                1,
                (int)Math.Ceiling(retryAfter.TotalSeconds));
            throw new RequestCooldownException(
                $"กรุณารออีก {seconds} วินาทีก่อนขอรหัสใหม่",
                retryAfter);
        }

        var challengeId = Convert.ToHexString(
            RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Challenges[challengeId] = new Entry(
            normalized,
            purpose,
            providerRequestKey,
            code,
            Hash(challengeId, code, purpose),
            now,
            now.Add(LifetimeFor(purpose)),
            0);
        LastRequests[requestKey] = now;
        Requests[(providerRequestKey, purpose)] =
            challengeId;

        return Task.FromResult(
            ToChallenge(challengeId, Challenges[challengeId]));
    }

    public Task<OtpChallengeRecovery?> LookupAsync(
        string providerRequestKey,
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        providerRequestKey = ValidProviderRequestKey(
            providerRequestKey);
        var normalized = NormalizeThaiPhone(phoneNumber);
        if (Requests.TryGetValue(
                (providerRequestKey, purpose),
                out var challengeId) &&
            Challenges.TryGetValue(challengeId, out var entry) &&
            string.Equals(
                entry.PhoneNumber,
                normalized,
                StringComparison.Ordinal) &&
            entry.ExpiresAt > _clock.UtcNow)
            return Task.FromResult<OtpChallengeRecovery?>(
                new OtpChallengeRecovery(
                    ToChallenge(challengeId, entry),
                    entry.ProviderRequestKey,
                    entry.Purpose,
                    entry.PhoneNumber,
                    entry.AcceptedAt,
                    entry.ExpiresAt));
        return Task.FromResult<OtpChallengeRecovery?>(null);
    }

    public Task<string?> VerifyAsync(
        string challengeId,
        string code,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (code.Length != 6 ||
            code.Any(character => !char.IsAsciiDigit(character)))
            return Task.FromResult<string?>(null);
        var supplied = Hash(challengeId, code, purpose);
        while (Challenges.TryGetValue(challengeId, out var entry))
        {
            if (entry.Purpose != purpose)
                return Task.FromResult<string?>(null);
            if (entry.ExpiresAt <= _clock.UtcNow ||
                entry.Attempts >= 5)
            {
                TryRemoveExact(challengeId, entry);
                return Task.FromResult<string?>(null);
            }

            if (CryptographicOperations.FixedTimeEquals(
                    entry.CodeHash,
                    supplied))
            {
                if (TryRemoveExact(challengeId, entry))
                    return Task.FromResult<string?>(entry.PhoneNumber);
                continue;
            }

            if (Challenges.TryUpdate(
                    challengeId,
                    entry with { Attempts = entry.Attempts + 1 },
                    entry))
                return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(null);
    }

    public async Task<OtpProviderVerificationEvidence>
        VerifyIdempotentlyAsync(
            string challengeId,
            string code,
            OtpPurpose purpose,
            string verificationRequestKey,
            CancellationToken cancellationToken)
    {
        verificationRequestKey = ValidProviderRequestKey(
            verificationRequestKey);
        if (code.Length != 6 ||
            code.Any(character => !char.IsAsciiDigit(character)))
            throw new ArgumentException(
                "Verification code must contain six ASCII digits.",
                nameof(code));
        var codeHash = Hash(challengeId, code, purpose);
        var lazy = Verifications.GetOrAdd(
            (verificationRequestKey, purpose),
            _ => new Lazy<Task<VerificationEntry>>(
                () => VerifyOnceAsync(
                    challengeId,
                    code,
                    purpose,
                    verificationRequestKey),
                LazyThreadSafetyMode.ExecutionAndPublication));
        var stored = await lazy.Value.WaitAsync(cancellationToken);
        if (!string.Equals(
                stored.Evidence.ChallengeId,
                challengeId,
                StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(
                stored.CodeHash,
                codeHash))
            throw new ArgumentException(
                "Verification request key was already used for another request.",
                nameof(verificationRequestKey));
        return stored.Evidence;
    }

    public async Task<OtpProviderVerificationEvidence?>
        LookupVerificationAsync(
            string verificationRequestKey,
            string challengeId,
            string phoneNumber,
            OtpPurpose purpose,
            CancellationToken cancellationToken)
    {
        verificationRequestKey = ValidProviderRequestKey(
            verificationRequestKey);
        var normalizedPhone = NormalizeThaiPhone(phoneNumber);
        if (!Verifications.TryGetValue(
                (verificationRequestKey, purpose),
                out var lazy) ||
            !lazy.IsValueCreated)
            return null;
        var stored = await lazy.Value.WaitAsync(cancellationToken);
        return string.Equals(
                   stored.Evidence.ChallengeId,
                   challengeId,
                   StringComparison.Ordinal) &&
               string.Equals(
                   stored.Evidence.PhoneNumber,
                   normalizedPhone,
                   StringComparison.Ordinal)
            ? stored.Evidence
            : null;
    }

    private Task<VerificationEntry> VerifyOnceAsync(
        string challengeId,
        string code,
        OtpPurpose purpose,
        string verificationRequestKey)
    {
        var requestedAt = _clock.UtcNow;
        if (!Challenges.TryGetValue(challengeId, out var entry) ||
            entry.Purpose != purpose)
            throw new InvalidOperationException(
                "The OTP challenge is unavailable for authoritative verification.");
        var verifiedPhone = entry.PhoneNumber;
        var supplied = Hash(challengeId, code, purpose);
        var verified = false;
        while (Challenges.TryGetValue(challengeId, out entry))
        {
            if (entry.Purpose != purpose ||
                entry.ExpiresAt <= _clock.UtcNow ||
                entry.Attempts >= 5)
            {
                TryRemoveExact(challengeId, entry);
                break;
            }
            if (CryptographicOperations.FixedTimeEquals(
                    entry.CodeHash,
                    supplied))
            {
                if (TryRemoveExact(challengeId, entry))
                {
                    verified = true;
                    break;
                }
                continue;
            }
            if (Challenges.TryUpdate(
                    challengeId,
                    entry with { Attempts = entry.Attempts + 1 },
                    entry))
                break;
        }

        var evidence = new OtpProviderVerificationEvidence(
            verificationRequestKey,
            challengeId,
            purpose,
            verifiedPhone,
            verified
                ? OtpProviderVerificationOutcome.Verified
                : OtpProviderVerificationOutcome.Rejected,
            requestedAt,
            _clock.UtcNow);
        return Task.FromResult(
            new VerificationEntry(evidence, supplied));
    }

    public static string NormalizeThaiPhone(string value)
        => ThaiMobilePhone.Normalize(value);

    private static string Mask(string phone) =>
        $"0••-•••-{phone[^4..]}";

    private static OtpChallenge ToChallenge(
        string challengeId,
        Entry entry) =>
        new(
            challengeId,
            Mask(entry.PhoneNumber),
            entry.DevelopmentCode);

    private static string ValidProviderRequestKey(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new ArgumentException(
                "Provider request key must be a 32-character UUID.",
                nameof(value));
        return parsed.ToString("N");
    }

    private static TimeSpan LifetimeFor(OtpPurpose purpose) =>
        purpose == OtpPurpose.AccountNameChange
            ? AccountNameChangeLifetime
            : AuthenticationLifetime;

    private static bool TryRemoveExact(
        string challengeId,
        Entry entry) =>
        ((ICollection<KeyValuePair<string, Entry>>)Challenges)
        .Remove(new KeyValuePair<string, Entry>(challengeId, entry));

    private static byte[] Hash(
        string challengeId,
        string code,
        OtpPurpose purpose) =>
        SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(
                $"{purpose}:{challengeId}:{code}"));

    private sealed record Entry(
        string PhoneNumber,
        OtpPurpose Purpose,
        string ProviderRequestKey,
        string DevelopmentCode,
        byte[] CodeHash,
        DateTimeOffset AcceptedAt,
        DateTimeOffset ExpiresAt,
        int Attempts);

    private sealed record VerificationEntry(
        OtpProviderVerificationEvidence Evidence,
        byte[] CodeHash);
}
