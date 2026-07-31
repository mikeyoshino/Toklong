using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Toklong.Application.Abstractions;

namespace Toklong.Infrastructure.Security;

public sealed class AccountNameAuditEvidenceProtector
    : IAccountNameAuditEvidenceWriter
{
    public const string ProtectionVersion = "aspnet-dp:v1";
    public const string ProtectionPurpose =
        "Toklong.AccountNameAuditEvidence.v1";
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector;

    public AccountNameAuditEvidenceProtector(
        IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        protector = dataProtectionProvider.CreateProtector(
            ProtectionPurpose);
    }

    public ProtectedAccountNameAuditEvidence Protect(
        AccountNameAuditEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Validate(evidence);
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(
            evidence,
            JsonOptions);
        return new(
            protector.Protect(plaintext),
            ProtectionVersion);
    }

    public AccountNameAuditEvidence UnprotectForAuditReview(
        ProtectedAccountNameAuditEvidence protectedEvidence)
    {
        ArgumentNullException.ThrowIfNull(protectedEvidence);
        if (!string.Equals(
                protectedEvidence.ProtectionVersion,
                ProtectionVersion,
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Unsupported account-name audit protection version.");
        var plaintext = protector.Unprotect(
            protectedEvidence.Ciphertext);
        var evidence =
            JsonSerializer.Deserialize<AccountNameAuditEvidence>(
                plaintext,
                JsonOptions)
            ?? throw new InvalidOperationException(
                "Protected account-name audit evidence is invalid.");
        Validate(evidence);
        return evidence;
    }

    private static void Validate(AccountNameAuditEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.NewName) ||
            evidence.NewName.Length > 241 ||
            evidence.OldBuyerName?.Length > 241 ||
            evidence.OldSellerName?.Length > 241)
            throw new ArgumentException(
                "Account-name audit evidence is invalid.",
                nameof(evidence));
    }
}
