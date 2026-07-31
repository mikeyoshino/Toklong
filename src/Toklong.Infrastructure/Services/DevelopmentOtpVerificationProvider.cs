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
        new(true, TimeSpan.FromMinutes(10), true);

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
            now.Add(LifetimeFor(purpose)),
            0);
        LastRequests[requestKey] = now;
        Requests[(providerRequestKey, purpose)] =
            challengeId;

        return Task.FromResult(
            ToChallenge(challengeId, Challenges[challengeId]));
    }

    public Task<OtpChallenge?> LookupAsync(
        string providerRequestKey,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        providerRequestKey = ValidProviderRequestKey(
            providerRequestKey);
        if (Requests.TryGetValue(
                (providerRequestKey, purpose),
                out var challengeId) &&
            Challenges.TryGetValue(challengeId, out var entry) &&
            entry.ExpiresAt > _clock.UtcNow)
            return Task.FromResult<OtpChallenge?>(
                ToChallenge(challengeId, entry));
        return Task.FromResult<OtpChallenge?>(null);
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
        DateTimeOffset ExpiresAt,
        int Attempts);
}
