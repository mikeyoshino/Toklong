using Toklong.Crm.Persistence;

namespace Toklong.Crm.Disputes;

public enum CrmQueueOwnership
{
    All,
    Unassigned,
    Mine
}

public enum CrmQueueSort
{
    Priority,
    Oldest,
    Newest
}

public sealed record CrmQueueTableQuery(
    string Search,
    CrmDisputeCaseStatus? Status,
    CrmQueueOwnership Ownership,
    bool OverdueOnly,
    CrmQueueSort Sort,
    int Page,
    int PageSize,
    Guid? CurrentUserId,
    DateTimeOffset Now);

public sealed record CrmQueueTablePage(
    IReadOnlyList<CrmDisputeQueueItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public static class CrmQueueTable
{
    public static CrmQueueTablePage Apply(
        IEnumerable<CrmDisputeQueueItem> source,
        CrmQueueTableQuery query)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        var search = query.Search.Trim();
        var filtered = source.Where(item =>
            MatchesSearch(item, search) &&
            (!query.Status.HasValue ||
             item.Status == query.Status.Value) &&
            MatchesOwnership(
                item,
                query.Ownership,
                query.CurrentUserId) &&
            (!query.OverdueOnly ||
             IsOverdue(item, query.Now)));
        var ordered = query.Sort switch
        {
            CrmQueueSort.Oldest => filtered
                .OrderBy(item => item.OpenedAt),
            CrmQueueSort.Newest => filtered
                .OrderByDescending(item => item.OpenedAt),
            _ => filtered
                .OrderByDescending(item =>
                    IsOverdue(item, query.Now))
                .ThenBy(item => NextDueAt(item))
                .ThenBy(item => item.OpenedAt)
        };
        var totalCount = ordered.Count();
        var pageSize = Math.Clamp(query.PageSize, 10, 100);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)pageSize));
        var page = Math.Clamp(query.Page, 1, totalPages);
        return new CrmQueueTablePage(
            ordered
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList(),
            totalCount,
            page,
            pageSize);
    }

    public static DateTimeOffset NextDueAt(
        CrmDisputeQueueItem item) =>
        item.Status switch
        {
            CrmDisputeCaseStatus.Open =>
                item.AssignmentDueAt,
            CrmDisputeCaseStatus.ReadyForApproval
                when item.ApprovalDueAt.HasValue =>
                item.ApprovalDueAt.Value,
            _ => item.FirstReviewDueAt
        };

    public static bool IsOverdue(
        CrmDisputeQueueItem item,
        DateTimeOffset now) =>
        item.Status is not
            (CrmDisputeCaseStatus.Approved or
             CrmDisputeCaseStatus.Closed) &&
        now > NextDueAt(item);

    private static bool MatchesSearch(
        CrmDisputeQueueItem item,
        string search)
    {
        if (search.Length == 0)
            return true;
        return item.CaseNumber.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ||
               item.ProductName.Contains(
                   search,
                   StringComparison.OrdinalIgnoreCase) ||
               (item.Reason?.ToString().Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ??
                false) ||
               (item.AssignedDisplayName?.Contains(
                    search,
                    StringComparison.OrdinalIgnoreCase) ??
                false);
    }

    private static bool MatchesOwnership(
        CrmDisputeQueueItem item,
        CrmQueueOwnership ownership,
        Guid? currentUserId) =>
        ownership switch
        {
            CrmQueueOwnership.Unassigned =>
                !item.AssignedUserId.HasValue,
            CrmQueueOwnership.Mine =>
                currentUserId.HasValue &&
                item.AssignedUserId == currentUserId,
            _ => true
        };
}
