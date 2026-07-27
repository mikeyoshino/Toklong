using Toklong.Crm.Disputes;
using Toklong.Crm.Persistence;
using Toklong.Domain.Transactions;

namespace Toklong.Crm.Tests.Disputes;

public sealed class CrmQueueTableTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 27, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Pagination_returns_only_requested_page()
    {
        var items = Enumerable.Range(1, 35)
            .Select(index => Item(index))
            .ToArray();

        var result = CrmQueueTable.Apply(
            items,
            Query(page: 2, pageSize: 20));

        Assert.Equal(35, result.TotalCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(15, result.Items.Count);
    }

    [Fact]
    public void Search_status_and_ownership_filters_compose()
    {
        var currentUserId = Guid.NewGuid();
        var items = new[]
        {
            Item(
                1,
                "กล้อง Leica",
                CrmDisputeCaseStatus.InReview,
                currentUserId,
                "Admin Local"),
            Item(
                2,
                "กล้อง Fujifilm",
                CrmDisputeCaseStatus.Open),
            Item(
                3,
                "โทรศัพท์",
                CrmDisputeCaseStatus.InReview,
                currentUserId,
                "Admin Local")
        };

        var result = CrmQueueTable.Apply(
            items,
            Query(
                search: "กล้อง",
                status: CrmDisputeCaseStatus.InReview,
                ownership: CrmQueueOwnership.Mine,
                currentUserId: currentUserId));

        var item = Assert.Single(result.Items);
        Assert.Equal("DSP-0001", item.CaseNumber);
    }

    [Fact]
    public void Priority_sort_places_overdue_case_first()
    {
        var overdue = Item(
            1,
            assignmentDueAt: Now.AddMinutes(-1));
        var future = Item(
            2,
            assignmentDueAt: Now.AddHours(1));

        var result = CrmQueueTable.Apply(
            [future, overdue],
            Query(sort: CrmQueueSort.Priority));

        Assert.Equal(overdue.CaseId, result.Items[0].CaseId);
        Assert.True(CrmQueueTable.IsOverdue(overdue, Now));
        Assert.False(CrmQueueTable.IsOverdue(future, Now));
    }

    private static CrmQueueTableQuery Query(
        string search = "",
        CrmDisputeCaseStatus? status = null,
        CrmQueueOwnership ownership =
            CrmQueueOwnership.All,
        CrmQueueSort sort = CrmQueueSort.Oldest,
        int page = 1,
        int pageSize = 20,
        Guid? currentUserId = null) =>
        new(
            search,
            status,
            ownership,
            false,
            sort,
            page,
            pageSize,
            currentUserId,
            Now);

    private static CrmDisputeQueueItem Item(
        int index,
        string productName = "สินค้า",
        CrmDisputeCaseStatus status =
            CrmDisputeCaseStatus.Open,
        Guid? assignedUserId = null,
        string? assignedDisplayName = null,
        DateTimeOffset? assignmentDueAt = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"DSP-{index:0000}",
            status,
            productName,
            DisputeReason.NotAsDescribed,
            100_00,
            "THB",
            Now.AddDays(-index),
            assignmentDueAt ?? Now.AddHours(index),
            Now.AddDays(1),
            null,
            assignedUserId,
            assignedDisplayName);
}
