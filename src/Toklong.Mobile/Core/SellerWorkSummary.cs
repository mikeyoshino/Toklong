namespace Toklong.Mobile.Core;

public enum SellerWorkCategory
{
    All,
    NewOffers,
    FulfillmentRequired,
    InProgress,
    Problems
}

public sealed record SellerWorkSnapshot(
    int TotalCount,
    int NewOfferCount,
    int FulfillmentRequiredCount,
    int InProgressCount,
    int ProblemCount,
    int ActionableCount,
    SellerWorkCategory SelectedCategory,
    AppTransaction? Spotlight,
    IReadOnlyList<AppTransaction> AllSellerTransactions,
    IReadOnlyList<AppTransaction> VisibleTransactions,
    IReadOnlyList<AppTransaction> RemainingTransactions);

public static class SellerWorkSummary
{
    public static SellerWorkCategory? CategoryOf(AppTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Role != AppTransactionRole.Seller)
            return null;
        if (transaction.State is "Disputed" or "ResolutionPending")
            return SellerWorkCategory.Problems;

        return transaction.Presentation.PrimaryAction switch
        {
            TransactionAction.ReviewSellerOffer =>
                SellerWorkCategory.NewOffers,
            TransactionAction.AddTracking or
                TransactionAction.ConfirmDigitalHandoff =>
                SellerWorkCategory.FulfillmentRequired,
            _ when transaction.Presentation.Bucket ==
                   TransactionBucket.Completed =>
                null,
            _ => SellerWorkCategory.InProgress
        };
    }

    public static SellerWorkSnapshot Create(
        IEnumerable<AppTransaction> source,
        SellerWorkCategory selectedCategory =
            SellerWorkCategory.All)
    {
        ArgumentNullException.ThrowIfNull(source);
        var seller = source
            .Where(item => item.Role == AppTransactionRole.Seller)
            .ToArray();
        var categorized = seller
            .Select(item => (Item: item, Category: CategoryOf(item)))
            .ToArray();
        var newOfferCount = categorized.Count(
            value => value.Category == SellerWorkCategory.NewOffers);
        var fulfillmentCount = categorized.Count(
            value => value.Category ==
                     SellerWorkCategory.FulfillmentRequired);
        var inProgressCount = categorized.Count(
            value => value.Category == SellerWorkCategory.InProgress);
        var problemCount = categorized.Count(
            value => value.Category == SellerWorkCategory.Problems);
        var visible = selectedCategory == SellerWorkCategory.All
            ? seller
            : categorized
                .Where(value => value.Category == selectedCategory)
                .Select(value => value.Item)
                .ToArray();
        var newestFirst = visible
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Id)
            .ToArray();
        var spotlight = visible
            .OrderBy(Priority)
            .ThenBy(item =>
                Priority(item) < 3
                    ? item.ActionDeadline ?? DateTimeOffset.MaxValue
                    : DateTimeOffset.MaxValue)
            .ThenByDescending(item =>
                Priority(item) == 3
                    ? item.UpdatedAt
                    : item.CreatedAt)
            .ThenBy(item => item.Id)
            .FirstOrDefault(item =>
                item.Presentation.PrimaryAction is
                    TransactionAction.ReviewSellerOffer or
                    TransactionAction.AddTracking or
                    TransactionAction.ConfirmDigitalHandoff);
        var remaining = spotlight is null
            ? newestFirst
            : newestFirst
                .Where(item => item.Id != spotlight.Id)
                .ToArray();

        return new SellerWorkSnapshot(
            seller.Length,
            newOfferCount,
            fulfillmentCount,
            inProgressCount,
            problemCount,
            newOfferCount + fulfillmentCount,
            selectedCategory,
            spotlight,
            seller,
            newestFirst,
            remaining);
    }

    private static int Priority(AppTransaction item) =>
        item.State is "ShipmentOverdue" ||
        (item.State == "TrackingUnverified" &&
         item.Presentation.PrimaryAction == TransactionAction.AddTracking)
            ? 0
            : item.Presentation.PrimaryAction is
                TransactionAction.AddTracking or
                TransactionAction.ConfirmDigitalHandoff
                ? 1
                : item.Presentation.PrimaryAction ==
                  TransactionAction.ReviewSellerOffer
                    ? 2
                    : 3;
}
