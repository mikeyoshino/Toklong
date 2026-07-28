using System.Security.Cryptography;
using System.Text;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Email;

public sealed class HmacEmailVerificationCodeService
    : IEmailVerificationCodeService
{
    private readonly byte[] _digestKey;

    public HmacEmailVerificationCodeService(
        EmailVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _digestKey = Encoding.UTF8.GetBytes(options.DigestKey);
        if (_digestKey.Length < 32)
            throw new InvalidOperationException(
                "EmailVerification:DigestKey must be at least 32 UTF-8 bytes");
    }

    public EmailVerificationCodePair Issue(Guid challengeId)
    {
        var code = RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");
        return new EmailVerificationCodePair(
            code,
            Digest(challengeId, code));
    }

    public string Digest(Guid challengeId, string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return Compute($"{challengeId:N}:{code}");
    }

    public string HashDestination(string normalizedEmail)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);
        return Compute($"destination:{normalizedEmail}");
    }

    private string Compute(string value) =>
        Convert.ToHexString(
                HMACSHA256.HashData(
                    _digestKey,
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
