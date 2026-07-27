namespace Toklong.Crm.Persistence;

public sealed class CrmSession
{
    private CrmSession() { }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TicketHash { get; private set; } = "";
    public byte[] ProtectedTicket { get; private set; } = [];
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset LastValidatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public DateTimeOffset? RevokedAt { get; private set; }
    public long Version { get; private set; }

    public static CrmSession Create(
        Guid userId,
        string ticketHash,
        byte[] protectedTicket,
        DateTimeOffset now,
        DateTimeOffset expiresAt)
    {
        if (expiresAt <= now)
            throw new InvalidOperationException(
                "CRM session expiry must be in the future.");
        return new CrmSession
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TicketHash = ticketHash,
            ProtectedTicket = protectedTicket,
            CreatedAt = now,
            LastValidatedAt = now,
            ExpiresAt = expiresAt
        };
    }

    public bool IsActive(DateTimeOffset now) =>
        RevokedAt is null && ExpiresAt > now;

    public void Renew(
        byte[] protectedTicket,
        DateTimeOffset expiresAt,
        DateTimeOffset now)
    {
        ProtectedTicket = protectedTicket;
        ExpiresAt = expiresAt;
        LastValidatedAt = now;
        Version++;
    }

    public bool MarkValidated(
        DateTimeOffset now,
        TimeSpan minimumInterval)
    {
        if (now - LastValidatedAt < minimumInterval)
            return false;
        LastValidatedAt = now;
        Version++;
        return true;
    }

    public void Revoke(DateTimeOffset now)
    {
        if (RevokedAt is not null)
            return;
        RevokedAt = now;
        Version++;
    }
}
