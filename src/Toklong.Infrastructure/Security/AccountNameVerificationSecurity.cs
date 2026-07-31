using System.Security.Cryptography;
using System.Text;
using Toklong.Application.Abstractions;
using Toklong.Infrastructure.Email;

namespace Toklong.Infrastructure.Security;

public sealed class AccountNameVerificationSecurity
    : IAccountNameVerificationSecurity
{
    private readonly byte[] _digestKey;

    public AccountNameVerificationSecurity(
        EmailVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _digestKey = Encoding.UTF8.GetBytes(options.DigestKey);
        if (_digestKey.Length < 32)
            throw new InvalidOperationException(
                "EmailVerification:DigestKey must be at least 32 UTF-8 bytes");
    }

    public string Digest(Guid challengeId, string code)
    {
        if (challengeId == Guid.Empty)
            throw new ArgumentException(
                "Challenge ID must not be empty.",
                nameof(challengeId));
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != 6 ||
            code.Any(character => !char.IsAsciiDigit(character)))
            throw new ArgumentException(
                "Verification code must contain six ASCII digits.",
                nameof(code));

        return Hmac($"account-name:{challengeId:N}:{code}");
    }

    public string DigestAuditValue(Guid challengeId, string value)
    {
        if (challengeId == Guid.Empty)
            throw new ArgumentException(
                "Challenge ID must not be empty.",
                nameof(challengeId));
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Hmac(
            $"account-name-audit:{challengeId:N}:{value.Trim()}");
    }

    private string Hmac(string value) =>
        Convert.ToHexString(
                HMACSHA256.HashData(
                    _digestKey,
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
