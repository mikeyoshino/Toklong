namespace Toklong.Application.Abstractions;

public sealed record AccountNameAuditEvidence(
    string? OldBuyerName,
    string? OldSellerName,
    string NewName);

public sealed record ProtectedAccountNameAuditEvidence(
    byte[] Ciphertext,
    string ProtectionVersion);

public interface IAccountNameAuditEvidenceWriter
{
    ProtectedAccountNameAuditEvidence Protect(
        AccountNameAuditEvidence evidence);
}
