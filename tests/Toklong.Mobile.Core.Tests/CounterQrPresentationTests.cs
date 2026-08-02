using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CounterQrPresentationTests
{
    [Fact]
    public void Expired_ready_counter_qr_is_an_error_and_not_displayable()
    {
        var transaction = PhysicalSeller("Ready") with
        {
            CounterQrExpiresAt =
                DateTimeOffset.UtcNow.AddMinutes(-1)
        };

        Assert.True(transaction.ShowCounterQrCard);
        Assert.False(transaction.IsCounterQrReady);
        Assert.True(transaction.IsCounterQrError);
    }

    [Theory]
    [InlineData("Pending", true, false, false)]
    [InlineData("Ready", false, true, false)]
    [InlineData("RetryableError", false, false, true)]
    [InlineData("Unavailable", false, false, true)]
    public void Seller_counter_qr_status_has_one_clear_presentation(
        string status,
        bool pending,
        bool ready,
        bool error)
    {
        var transaction = PhysicalSeller(status);

        Assert.True(transaction.ShowCounterQrCard);
        Assert.Equal(pending, transaction.IsCounterQrPending);
        Assert.Equal(ready, transaction.IsCounterQrReady);
        Assert.Equal(error, transaction.IsCounterQrError);
    }

    [Fact]
    public void Buyer_never_sees_counter_qr_card()
    {
        var transaction = PhysicalSeller("Ready") with
        {
            Role = AppTransactionRole.Buyer
        };

        Assert.False(transaction.ShowCounterQrCard);
    }

    [Fact]
    public void Counter_qr_analytics_are_coarse_and_have_no_properties()
    {
        var events = new[]
        {
            CounterQrAnalytics.ReadyViewed(),
            CounterQrAnalytics.FullscreenOpened(),
            CounterQrAnalytics.RetryRequested(),
            CounterQrAnalytics.LabelDownloadRequested()
        };

        Assert.All(events, value => Assert.Empty(value.Properties));
        Assert.Equal(
            new[]
            {
                "counter_qr_ready_viewed",
                "counter_qr_fullscreen",
                "counter_qr_retry_requested",
                "shipping_label_download_requested"
            },
            events.Select(value => value.Name));
    }

    private static AppTransaction PhysicalSeller(string status) =>
        new(
            Guid.NewGuid(),
            "กล้อง",
            100_000,
            "THB",
            AppTransactionRole.Seller,
            AppFulfillmentType.Physical,
            "TrackingSubmitted",
            DateTimeOffset.UtcNow,
            null,
            "ผู้ซื้อ")
        {
            ShippingManagedByProvider = true,
            CounterQrStatus = status
        };
}
