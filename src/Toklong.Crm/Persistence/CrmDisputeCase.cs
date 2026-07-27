namespace Toklong.Crm.Persistence;

public enum CrmDisputeCaseStatus
{
    Open,
    InReview,
    AwaitingEvidence,
    ReadyForApproval,
    Approved,
    Closed
}

public sealed class CrmDisputeCase
{
    private CrmDisputeCase() { }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public string CaseNumber { get; private set; } = "";
    public CrmDisputeCaseStatus Status { get; private set; }
    public Guid? AssignedUserId { get; private set; }
    public DateTimeOffset OpenedAt { get; private set; }
    public DateTimeOffset AssignmentDueAt { get; private set; }
    public DateTimeOffset FirstReviewDueAt { get; private set; }
    public DateTimeOffset? ReadyForApprovalAt { get; private set; }
    public DateTimeOffset? ApprovalDueAt { get; private set; }
    public DateTimeOffset? ClosedAt { get; private set; }
    public long Version { get; private set; }

    public static CrmDisputeCase Create(
        Guid transactionId,
        DateTimeOffset openedAt)
    {
        if (transactionId == Guid.Empty)
            throw new InvalidOperationException(
                "Transaction ID ไม่ถูกต้อง");
        return new CrmDisputeCase
        {
            Id = Guid.NewGuid(),
            TransactionId = transactionId,
            CaseNumber =
                $"DSP-{openedAt:yyyyMMdd}-{transactionId.ToString("N")[..8].ToUpperInvariant()}",
            Status = CrmDisputeCaseStatus.Open,
            OpenedAt = openedAt,
            AssignmentDueAt = AddBusinessHours(
                openedAt,
                4),
            FirstReviewDueAt = AddBusinessDays(
                openedAt,
                1)
        };
    }

    public void Claim(Guid actorUserId)
    {
        EnsureOpen();
        AssignedUserId = actorUserId;
        if (Status == CrmDisputeCaseStatus.Open)
            Status = CrmDisputeCaseStatus.InReview;
        Version++;
    }

    public void AwaitEvidence()
    {
        EnsureOpen();
        Status = CrmDisputeCaseStatus.AwaitingEvidence;
        Version++;
    }

    public void ReadyForApproval(DateTimeOffset now)
    {
        EnsureOpen();
        Status = CrmDisputeCaseStatus.ReadyForApproval;
        ReadyForApprovalAt ??= now;
        ApprovalDueAt ??= AddBusinessDays(now, 2);
        Version++;
    }

    public void MarkApproved()
    {
        EnsureOpen();
        Status = CrmDisputeCaseStatus.Approved;
        Version++;
    }

    public void ReturnToReview()
    {
        EnsureOpen();
        Status = CrmDisputeCaseStatus.InReview;
        ReadyForApprovalAt = null;
        ApprovalDueAt = null;
        Version++;
    }

    public void Close(DateTimeOffset now)
    {
        Status = CrmDisputeCaseStatus.Closed;
        ClosedAt ??= now;
        Version++;
    }

    private void EnsureOpen()
    {
        if (Status == CrmDisputeCaseStatus.Closed)
            throw new InvalidOperationException(
                "เคสนี้ปิดแล้ว");
    }

    private static DateTimeOffset AddBusinessDays(
        DateTimeOffset value,
        int days)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            "Asia/Bangkok");
        var result = TimeZoneInfo.ConvertTime(value, zone);
        while (days > 0)
        {
            result = result.AddDays(1);
            if (result.DayOfWeek is not
                (DayOfWeek.Saturday or DayOfWeek.Sunday))
                days--;
        }
        return result.ToUniversalTime();
    }

    private static DateTimeOffset AddBusinessHours(
        DateTimeOffset value,
        int hours)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(
            "Asia/Bangkok");
        var result = TimeZoneInfo.ConvertTime(value, zone);
        var remaining = TimeSpan.FromHours(hours);
        while (remaining > TimeSpan.Zero)
        {
            if (result.DayOfWeek is DayOfWeek.Saturday or
                DayOfWeek.Sunday ||
                result.TimeOfDay >= TimeSpan.FromHours(18))
            {
                do
                {
                    result = StartOfDay(result).AddDays(1)
                        .AddHours(9);
                } while (result.DayOfWeek is
                         DayOfWeek.Saturday or
                         DayOfWeek.Sunday);
                continue;
            }
            if (result.TimeOfDay < TimeSpan.FromHours(9))
                result = StartOfDay(result).AddHours(9);
            var available =
                StartOfDay(result).AddHours(18) - result;
            var increment = remaining < available
                ? remaining
                : available;
            result = result.Add(increment);
            remaining -= increment;
        }
        return result.ToUniversalTime();
    }

    private static DateTimeOffset StartOfDay(
        DateTimeOffset value) =>
        new(
            value.Year,
            value.Month,
            value.Day,
            0,
            0,
            0,
            value.Offset);
}
