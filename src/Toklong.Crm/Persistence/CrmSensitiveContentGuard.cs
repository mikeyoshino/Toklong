namespace Toklong.Crm.Persistence;

public static class CrmSensitiveContentGuard
{
    public static string RejectReusableCredentials(
        string value) =>
        Toklong.Domain.Transactions
            .ReusableCredentialGuard.Reject(value);
}
