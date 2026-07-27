using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class SellerOfferLinkTests
{
    private const string Token =
        "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData(
        $"https://app.toklong.co.th/offer/{Token}")]
    [InlineData(
        $"https://toklong.co.th/offer/{Token}")]
    [InlineData(
        $"toklong://offer/{Token}")]
    public void SupportedLinksReturnOpaqueOfferToken(string raw)
    {
        Assert.True(
            SellerOfferLink.TryGetPublicToken(
                new Uri(raw),
                out var token));
        Assert.Equal(Token, token);
    }

    [Fact]
    public void CopiedLinkMayContainSurroundingWhitespace()
    {
        Assert.True(
            SellerOfferLink.TryGetPublicToken(
                $" \nhttps://toklong.co.th/offer/{Token}\t ",
                out var token));
        Assert.Equal(Token, token);
    }

    [Theory]
    [InlineData("https://evil.example/offer/0123456789abcdef0123456789abcdef")]
    [InlineData("https://app.toklong.co.th/payment/0123456789abcdef0123456789abcdef")]
    [InlineData("toklong://offer/not-a-token")]
    public void UntrustedOrMalformedLinksAreRejected(string raw)
    {
        Assert.False(
            SellerOfferLink.TryGetPublicToken(
                new Uri(raw),
                out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ข้อความจากแชตที่ไม่มีลิงก์")]
    public void EmptyOrNonLinkClipboardTextIsRejected(string? raw)
    {
        Assert.False(
            SellerOfferLink.TryGetPublicToken(raw, out _));
    }

    [Fact]
    public void TransactionPushLinkReturnsTransactionId()
    {
        var id = Guid.NewGuid();

        Assert.True(
            TransactionLink.TryGetTransactionId(
                new Uri($"toklong://transaction/{id:D}"),
                out var parsed));
        Assert.Equal(id, parsed);
    }

    [Theory]
    [InlineData("https://evil.example/transaction/21d02844-e788-4c4a-8184-21bb7a1a23e5")]
    [InlineData("toklong://offer/21d02844-e788-4c4a-8184-21bb7a1a23e5")]
    [InlineData("toklong://transaction/not-an-id")]
    public void UnsupportedTransactionPushLinksAreRejected(
        string raw)
    {
        Assert.False(
            TransactionLink.TryGetTransactionId(
                new Uri(raw),
                out _));
    }
}
