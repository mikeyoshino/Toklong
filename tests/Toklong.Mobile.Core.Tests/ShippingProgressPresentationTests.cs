namespace Toklong.Mobile.Core.Tests;

public sealed class ShippingProgressPresentationTests
{
    [Theory]
    [InlineData("TrackingSubmitted", false, 0, 1)]
    [InlineData("TrackingSubmitted", true, 1, 2)]
    [InlineData("InTransit", true, 2, 3)]
    [InlineData("CarrierException", true, 2, 3)]
    [InlineData("DeliveredDisputeWindow", true, 4, 0)]
    public void Managed_shipping_maps_to_four_consumer_milestones(
        string state,
        bool hasFirstScan,
        int completed,
        int active)
    {
        var transaction = NewTransaction(
            state,
            hasFirstScan
                ? new DateTimeOffset(
                    2026, 7, 29, 21, 0, 0,
                    TimeSpan.Zero)
                : null);

        Assert.True(transaction.ShowShippingProgress);
        Assert.Equal(
            completed,
            transaction.ShippingProgressCompletedThrough);
        Assert.Equal(
            active,
            transaction.ShippingProgressActiveStep);
    }

    [Theory]
    [InlineData("OutcomeUnknown")]
    [InlineData("NeedsReview")]
    public void Shipping_review_state_uses_plain_language(
        string operationStatus)
    {
        var transaction = NewTransaction(
            "AwaitingSellerAcceptance",
            null,
            operationStatus);

        Assert.Equal(
            "การจัดส่งต้องตรวจสอบ",
            transaction.Presentation.StatusLabel);
        Assert.Equal(
            "ดูรายละเอียด",
            transaction.Presentation.PrimaryActionLabel);
    }

    private static AppTransaction NewTransaction(
        string state,
        DateTimeOffset? firstScan,
        string? operationStatus = null) =>
        new(
            Guid.NewGuid(),
            "กล้อง",
            120_000,
            "THB",
            AppTransactionRole.Buyer,
            AppFulfillmentType.Physical,
            state,
            DateTimeOffset.UtcNow,
            null,
            "ผู้ขาย ทดสอบ",
            ShippingManagedByProvider: true,
            FirstCarrierScanAt: firstScan,
            ShippingOperationStatus: operationStatus);
}
