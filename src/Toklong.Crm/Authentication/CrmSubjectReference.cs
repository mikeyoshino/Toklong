using System.Security.Cryptography;
using System.Text;

namespace Toklong.Crm.Authentication;

public static class CrmSubjectReference
{
    public static string Hash(
        string tenantId,
        string objectId) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(
                        $"{tenantId.Trim().ToLowerInvariant()}|" +
                        objectId.Trim().ToLowerInvariant())))
            .ToLowerInvariant();
}
