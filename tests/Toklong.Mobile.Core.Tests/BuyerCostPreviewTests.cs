using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class BuyerCostPreviewTests
{
    [Fact]
    public void Preview_formats_server_amounts_and_fulfillment_copy()
    {
        var preview = new BuyerCostPreview(
            itemPriceSatang: 500_000,
            buyerProtectionFeeSatang: 20_000,
            platformFeeSatang: 0,
            sellerExpectedNetSatang: 500_000,
            totalBeforeShippingSatang: 520_000,
            currency: "THB",
            feePolicyVersion: "buyer-protection-v2");

        Assert.Equal("฿5,000", preview.FormattedItemPrice);
        Assert.Equal("฿200", preview.FormattedProtectionFee);
        Assert.Equal("฿5,200", preview.FormattedTotalBeforeShipping);
        Assert.Equal(
            "ยอดก่อนค่าจัดส่ง",
            preview.SummaryLabel(AppFulfillmentType.Physical));
        Assert.Equal(
            "รอผู้ขายเลือก",
            preview.ShippingText(AppFulfillmentType.Physical));
        Assert.Equal(
            "ยอดเมื่อผู้ขายตอบรับ",
            preview.SummaryLabel(AppFulfillmentType.Digital));
        Assert.Equal(
            "ไม่มีค่าจัดส่ง",
            preview.ShippingText(AppFulfillmentType.Digital));
    }

    [Fact]
    public void Preview_rejects_total_that_does_not_match_server_breakdown()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            new BuyerCostPreview(
                itemPriceSatang: 500_000,
                buyerProtectionFeeSatang: 20_000,
                platformFeeSatang: 0,
                sellerExpectedNetSatang: 500_000,
                totalBeforeShippingSatang: 500_000,
                currency: "THB",
                feePolicyVersion: "buyer-protection-v2"));

        Assert.Equal(
            "ยอดรวมก่อนค่าจัดส่งไม่ตรงกับราคาสินค้าและค่าคุ้มครอง",
            exception.Message);
    }

    [Fact]
    public void Request_tracker_rejects_response_from_older_price()
    {
        var tracker = new BuyerCostPreviewRequestTracker();
        var firstRequest = tracker.Begin();
        var latestRequest = tracker.Begin();

        Assert.False(tracker.IsCurrent(firstRequest));
        Assert.True(tracker.IsCurrent(latestRequest));

        tracker.Invalidate();

        Assert.False(tracker.IsCurrent(latestRequest));
    }

    [Fact]
    public void Preview_deserializes_the_mobile_api_contract()
    {
        const string json =
            """
            {
              "itemPriceSatang": 500000,
              "buyerProtectionFeeSatang": 20000,
              "platformFeeSatang": 0,
              "sellerExpectedNetSatang": 500000,
              "totalBeforeShippingSatang": 520000,
              "currency": "THB",
              "feePolicyVersion": "buyer-protection-v2"
            }
            """;

        var preview = System.Text.Json.JsonSerializer
            .Deserialize<BuyerCostPreview>(
                json,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(preview);
        Assert.Equal("฿5,200", preview.FormattedTotalBeforeShipping);
    }
}
