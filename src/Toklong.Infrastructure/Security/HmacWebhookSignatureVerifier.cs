using System.Security.Cryptography;
using System.Text;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Security;

public sealed class ReconciliationOptions
{
    public const string SectionName = "Reconciliation";
    public string SigningSecret { get; set; } = "";
}

public sealed class HmacWebhookSignatureVerifier(ReconciliationOptions options) : IWebhookSignatureVerifier
{
    public bool Verify(string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(options.SigningSecret) || string.IsNullOrWhiteSpace(signature))
            return false;

        var expected = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(options.SigningSecret),
            Encoding.UTF8.GetBytes(payload));
        byte[] supplied;
        try
        {
            supplied = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(expected, supplied);
    }
}
