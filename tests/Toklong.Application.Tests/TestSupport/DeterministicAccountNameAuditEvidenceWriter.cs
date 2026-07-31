using System.Security.Cryptography;
using System.Text;
using Toklong.Application.Abstractions;

namespace Toklong.Application.Tests.TestSupport;

internal sealed class DeterministicAccountNameAuditEvidenceWriter
    : IAccountNameAuditEvidenceWriter
{
    public ProtectedAccountNameAuditEvidence Protect(
        AccountNameAuditEvidence evidence) =>
        new(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    $"{evidence.OldBuyerName}\n" +
                    $"{evidence.OldSellerName}\n" +
                    evidence.NewName)),
            "test:v1");
}
