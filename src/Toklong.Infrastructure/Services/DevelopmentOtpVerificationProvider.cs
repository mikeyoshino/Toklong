using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;

namespace Toklong.Infrastructure.Services;

public sealed class DevelopmentOtpVerificationProvider(IHostEnvironment environment)
    : IOtpVerificationProvider
{
    private static readonly ConcurrentDictionary<string, Entry> Challenges = new();
    private static readonly ConcurrentDictionary<string, DateTimeOffset> LastRequests = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ResendDelay = TimeSpan.FromSeconds(60);

    public Task<OtpChallenge> RequestAsync(
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
            throw new InvalidOperationException(
                "ยังไม่ได้ตั้งค่าผู้ให้บริการ OTP สำหรับ production");

        var normalized = NormalizeThaiPhone(phoneNumber);
        var now = DateTimeOffset.UtcNow;
        if (LastRequests.TryGetValue(normalized, out var last) &&
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
            Hash(challengeId, code),
            now.Add(Lifetime),
            0);
        LastRequests[normalized] = now;

        return Task.FromResult(new OtpChallenge(
            challengeId,
            Mask(normalized),
            code));
    }

    public Task<string?> VerifyAsync(
        string challengeId,
        string code,
        CancellationToken cancellationToken)
    {
        if (!Challenges.TryGetValue(challengeId, out var entry) ||
            entry.ExpiresAt <= DateTimeOffset.UtcNow ||
            entry.Attempts >= 5)
        {
            Challenges.TryRemove(challengeId, out _);
            return Task.FromResult<string?>(null);
        }

        var supplied = Hash(challengeId, code.Trim());
        if (!CryptographicOperations.FixedTimeEquals(entry.CodeHash, supplied))
        {
            Challenges[challengeId] = entry with { Attempts = entry.Attempts + 1 };
            return Task.FromResult<string?>(null);
        }

        Challenges.TryRemove(challengeId, out _);
        return Task.FromResult<string?>(entry.PhoneNumber);
    }

    public static string NormalizeThaiPhone(string value)
        => ThaiMobilePhone.Normalize(value);

    private static string Mask(string phone) =>
        $"0••-•••-{phone[^4..]}";

    private static byte[] Hash(string challengeId, string code) =>
        SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{challengeId}:{code}"));

    private sealed record Entry(
        string PhoneNumber,
        byte[] CodeHash,
        DateTimeOffset ExpiresAt,
        int Attempts);
}
