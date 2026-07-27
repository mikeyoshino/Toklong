namespace Toklong.Mobile.Core;

public enum RoleFilter
{
    All,
    Buying,
    Selling
}

public enum BucketFilter
{
    All,
    ActionRequired,
    InProgress,
    Completed,
    SellerReview,
    SellerFulfillment,
    SellerPayout
}

public static class TransactionFilter
{
    public static AppTransaction? FindActionRequired(
        IEnumerable<AppTransaction> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source.FirstOrDefault(item =>
            item.Presentation.Bucket == TransactionBucket.ActionRequired);
    }

    public static IReadOnlyList<AppTransaction> Apply(
        IEnumerable<AppTransaction> source,
        RoleFilter role,
        BucketFilter bucket)
    {
        ArgumentNullException.ThrowIfNull(source);

        return source
            .Where(item => role switch
            {
                RoleFilter.Buying => item.Role == AppTransactionRole.Buyer,
                RoleFilter.Selling => item.Role == AppTransactionRole.Seller,
                _ => true
            })
            .Where(item => bucket switch
            {
                BucketFilter.ActionRequired =>
                    item.Presentation.Bucket == TransactionBucket.ActionRequired,
                BucketFilter.InProgress =>
                    item.Presentation.Bucket == TransactionBucket.InProgress,
                BucketFilter.Completed =>
                    item.Presentation.Bucket == TransactionBucket.Completed,
                BucketFilter.SellerReview =>
                    item.Role == AppTransactionRole.Seller &&
                    item.Presentation.PrimaryAction ==
                        TransactionAction.ReviewSellerOffer,
                BucketFilter.SellerFulfillment =>
                    item.Role == AppTransactionRole.Seller &&
                    item.Presentation.PrimaryAction is
                        TransactionAction.AddTracking or
                        TransactionAction.ConfirmDigitalHandoff,
                BucketFilter.SellerPayout =>
                    item.Role == AppTransactionRole.Seller &&
                    item.Presentation.Bucket ==
                        TransactionBucket.InProgress,
                _ => true
            })
            .OrderByDescending(item =>
                item.CreatedAt == default
                    ? item.UpdatedAt
                    : item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToList();
    }
}
