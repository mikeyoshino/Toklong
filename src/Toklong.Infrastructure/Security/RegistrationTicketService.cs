using System.Security.Cryptography;
using System.Text;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Security;

public sealed class RegistrationTicketService : IRegistrationTicketService
{
    public RegistrationTicketPair Issue()
    {
        var rawTicket = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new RegistrationTicketPair(
            rawTicket,
            Hash(rawTicket));
    }

    public string Hash(string rawTicket)
    {
        if (string.IsNullOrWhiteSpace(rawTicket))
            throw new ArgumentException(
                "Registration ticket ไม่ถูกต้อง",
                nameof(rawTicket));

        return Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(rawTicket)))
            .ToLowerInvariant();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
