using System.Security.Cryptography;
using System.Text;

namespace Toklong.Crm.Disputes;

public static class CrmAuditReference
{
    public static string FromActorId(string actorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        var hash = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(actorId)))
            .ToLowerInvariant();
        return $"ref-{hash[..12]}";
    }
}
