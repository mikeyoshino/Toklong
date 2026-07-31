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

    public Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        OtpPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (!_environment.IsDevelopment())
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่าผู้ให้บริการ OTP สำหรับ production");

        var normalized = NormalizeThaiPhone(phoneNumber);
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
            Hash(challengeId, code, purpose),
            now.Add(LifetimeFor(purpose)),
            0);
        LastRequests[requestKey] = now;

        return Task.FromResult(new OtpChallenge(
            challengeId,
            Mask(normalized),
            code));
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
        byte[] CodeHash,
        DateTimeOffset ExpiresAt,
        int Attempts);
}
