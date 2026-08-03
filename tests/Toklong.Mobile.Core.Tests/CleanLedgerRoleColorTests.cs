using Toklong.Mobile.Core;

namespace Toklong.Mobile.Core.Tests;

public sealed class CleanLedgerRoleColorTests
{
    [Fact]
    public void Buyer_and_seller_transactions_use_the_approved_distinct_role_colors()
    {
        var buyer = Create(AppTransactionRole.Buyer);
        var seller = Create(AppTransactionRole.Seller);

        Assert.Equal("#1988D3", buyer.RoleColor);
        Assert.Equal("#E9F6FF", buyer.RoleBackground);
        Assert.Equal("#12364F", buyer.RoleHeaderStart);
        Assert.Equal("#14608A", buyer.RoleHeaderMiddle);
        Assert.Equal("#1988D3", buyer.RoleHeaderEnd);

        Assert.Equal("#55508A", seller.RoleColor);
        Assert.Equal("#EFEDFB", seller.RoleBackground);
        Assert.Equal("#302D56", seller.RoleHeaderStart);
        Assert.Equal("#45416F", seller.RoleHeaderMiddle);
        Assert.Equal("#55508A", seller.RoleHeaderEnd);
    }

    [Fact]
    public void Completed_progress_uses_the_transaction_role_palette()
    {
        var buyer = Create(AppTransactionRole.Buyer);
        var seller = Create(AppTransactionRole.Seller);

        Assert.Equal("#1988D3", buyer.ProgressOne.StrokeColor);
        Assert.Equal("#E9F6FF", buyer.ProgressOne.BackgroundColor);
        Assert.Equal("#55508A", seller.ProgressOne.StrokeColor);
        Assert.Equal("#EFEDFB", seller.ProgressOne.BackgroundColor);
    }

    private static AppTransaction Create(AppTransactionRole role) =>
        new(
            Guid.Parse("00000000-0000-0000-0000-0000000000A1"),
            "กล้องทดสอบ",
            3_000_000,
            "THB",
            role,
            AppFulfillmentType.Physical,
            "PaidOut",
            DateTimeOffset.Parse("2026-07-28T12:00:00+07:00"),
            DateTimeOffset.Parse("2026-07-29T18:00:00+07:00"),
            "คู่สัญญาทดสอบ");
}
