using System.Security.Cryptography;
using System.Text;
using Toklong.Infrastructure.Security;

namespace Toklong.Application.Tests.Security;

public sealed class HmacWebhookSignatureVerifierTests
{
    [Fact]
    public void Valid_signature_is_accepted_and_tampered_payload_is_rejected()
    {
        const string secret = "test-secret-only";
        const string payload = "payment|abc|event-1|12345";
        var signature = Convert.ToHexString(HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(payload)));
        var verifier = new HmacWebhookSignatureVerifier(new ReconciliationOptions { SigningSecret = secret });

        Assert.True(verifier.Verify(payload, signature));
        Assert.False(verifier.Verify(payload + "-tampered", signature));
        Assert.False(verifier.Verify(payload, "not-hex"));
    }
}
