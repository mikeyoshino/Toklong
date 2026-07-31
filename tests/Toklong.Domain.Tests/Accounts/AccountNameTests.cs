using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;

namespace Toklong.Domain.Tests.Accounts;

public sealed class AccountNameTests
{
    private static readonly DateTimeOffset ChangedAt =
        new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("  สมชาย ", " ใจดี  ", "สมชาย", "ใจดี", "สมชาย ใจดี")]
    [InlineData("Jean  Luc", "O’Neill-Smith", "Jean Luc", "O’Neill-Smith", "Jean Luc O’Neill-Smith")]
    public void Normalizes_supported_names(
        string first,
        string last,
        string expectedFirst,
        string expectedLast,
        string expectedDisplay)
    {
        var name = AccountName.Create(first, last);

        Assert.Equal(expectedFirst, name.FirstName);
        Assert.Equal(expectedLast, name.LastName);
        Assert.Equal(expectedDisplay, name.DisplayName);
    }

    [Theory]
    [InlineData("", "ใจดี")]
    [InlineData("สมชาย1", "ใจดี")]
    [InlineData("สมชาย😀", "ใจดี")]
    public void Rejects_missing_or_unsupported_characters(string first, string last) =>
        Assert.Throws<DomainException>(() => AccountName.Create(first, last));

    [Fact]
    public void Rejects_a_part_longer_than_sixty_characters() =>
        Assert.Throws<DomainException>(() =>
            AccountName.Create(new string('A', 61), "Smith"));

    [Fact]
    public void Rejects_a_combined_name_longer_than_one_hundred_twenty_characters() =>
        Assert.Throws<DomainException>(() =>
            AccountName.Create(new string('A', 60), new string('B', 60)));

    [Fact]
    public void Accepts_thai_combining_marks()
    {
        var name = AccountName.Create("น้อง", "ใจดี");

        Assert.Equal("น้อง ใจดี", name.DisplayName);
    }

    [Fact]
    public void Applying_an_account_name_updates_compatibility_display_fields()
    {
        var original = AccountName.Create("สมชาย", "ใจดี");
        var updated = AccountName.Create("สมหญิง", "มั่นคง");
        var buyer = BuyerAccount.Create(
            "+66812345678", original, "buyer@example.com", ChangedAt);
        var seller = SellerAccount.Create("+66812345678", ChangedAt, original);

        buyer.ApplyAccountName(updated, ChangedAt.AddMinutes(1));
        seller.ApplyAccountName(updated, ChangedAt.AddMinutes(1));

        Assert.Equal("สมหญิง", buyer.FirstName);
        Assert.Equal("มั่นคง", buyer.LastName);
        Assert.Equal("สมหญิง มั่นคง", buyer.FullName);
        Assert.Equal(ChangedAt.AddMinutes(1), buyer.NameChangedAt);
        Assert.Equal("สมหญิง", seller.FirstName);
        Assert.Equal("มั่นคง", seller.LastName);
        Assert.Equal("สมหญิง มั่นคง", seller.DisplayName);
        Assert.Equal(ChangedAt.AddMinutes(1), seller.NameChangedAt);
    }

    [Fact]
    public void Seller_without_a_registered_name_keeps_its_synthetic_display_name_outside_structured_fields()
    {
        var seller = SellerAccount.Create(
            "+66812345678",
            ChangedAt,
            (string?)null);

        Assert.Equal("ผู้ขาย 5678", seller.DisplayName);
        Assert.Equal("", seller.FirstName);
        Assert.Equal("", seller.LastName);

        seller.ApplyAccountName(
            AccountName.Create("สมชาย", "ใจดี"),
            ChangedAt.AddMinutes(1));

        Assert.Equal("สมชาย", seller.FirstName);
        Assert.Equal("ใจดี", seller.LastName);
        Assert.Equal("สมชาย ใจดี", seller.DisplayName);
    }

    [Fact]
    public void Legacy_unsplittable_seller_display_name_remains_display_only()
    {
        var seller = SellerAccount.Create(
            "+66812345678",
            ChangedAt,
            "ผู้ขายคุ้มครอง");

        Assert.Equal("ผู้ขายคุ้มครอง", seller.DisplayName);
        Assert.Equal("", seller.FirstName);
        Assert.Equal("", seller.LastName);
    }
}
