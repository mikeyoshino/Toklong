namespace Toklong.Crm.Persistence;

public enum CrmUserStatus
{
    Active,
    Disabled
}

public sealed class CrmUser
{
    private CrmUser() { }

    public Guid Id { get; private set; }
    public string EntraTenantId { get; private set; } = "";
    public string EntraObjectId { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string DisplayName { get; private set; } = "";
    public CrmUserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset? DisabledAt { get; private set; }
    public Guid? DisabledByUserId { get; private set; }
    public long Version { get; private set; }

    public static CrmUser Create(
        string entraTenantId,
        string entraObjectId,
        string email,
        string displayName,
        Guid? createdByUserId,
        DateTimeOffset now)
    {
        return new CrmUser
        {
            Id = Guid.NewGuid(),
            EntraTenantId = RequiredGuid(
                entraTenantId,
                "Entra tenant ID"),
            EntraObjectId = RequiredGuid(
                entraObjectId,
                "Entra object ID"),
            Email = Required(email, "อีเมล").ToLowerInvariant(),
            DisplayName = Required(displayName, "ชื่อผู้ใช้"),
            Status = CrmUserStatus.Active,
            CreatedAt = now,
            CreatedByUserId = createdByUserId
        };
    }

    public bool IsActive => Status == CrmUserStatus.Active;

    public void Disable(
        Guid disabledByUserId,
        DateTimeOffset now)
    {
        if (!IsActive)
            return;
        Status = CrmUserStatus.Disabled;
        DisabledAt = now;
        DisabledByUserId = disabledByUserId;
        Version++;
    }

    public void Reactivate()
    {
        if (IsActive)
            return;
        Status = CrmUserStatus.Active;
        DisabledAt = null;
        DisabledByUserId = null;
        Version++;
    }

    private static string Required(
        string? value,
        string label)
    {
        var clean = value?.Trim() ?? "";
        return clean.Length > 0
            ? clean
            : throw new InvalidOperationException(
                $"{label}ไม่ถูกต้อง");
    }

    private static string RequiredGuid(
        string? value,
        string label)
    {
        var clean = Required(value, label);
        return Guid.TryParse(clean, out var parsed)
            ? parsed.ToString("D")
            : throw new InvalidOperationException(
                $"{label}ไม่ถูกต้อง");
    }
}
