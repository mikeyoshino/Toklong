using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Services;

namespace Toklong.Application.Tests.Buyers;

public sealed class BuyerAddressTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Bundled_catalog_resolves_a_valid_hierarchy_and_postal_code()
    {
        var catalog = new BundledThaiAddressCatalog();

        var address = catalog.Resolve(
            "123 ถนนตัวอย่าง",
            1,
            1001,
            100101);

        Assert.Equal("กรุงเทพมหานคร", address.ProvinceName);
        Assert.Equal("เขตพระนคร", address.DistrictName);
        Assert.Equal("พระบรมมหาราชวัง", address.SubdistrictName);
        Assert.Equal("10200", address.PostalCode);
        Assert.Equal(77, catalog.Provinces.Count);
    }

    [Fact]
    public void Bundled_catalog_rejects_a_child_from_another_parent()
    {
        var catalog = new BundledThaiAddressCatalog();

        Assert.Throws<DomainException>(() =>
            catalog.Resolve(
                "123 ถนนตัวอย่าง",
                1,
                1001,
                200101));
    }

    [Fact]
    public void Saving_again_replaces_the_only_buyer_address()
    {
        var buyer = BuyerAccount.Create(
            "+66812345678",
            "สมชาย ใจดี",
            "buyer@example.com",
            Now);
        var catalog = new BundledThaiAddressCatalog();
        buyer.UpdateSavedDeliveryAddress(
            catalog.Resolve("123 ถนนเดิม", 1, 1001, 100101),
            Now);

        buyer.UpdateSavedDeliveryAddress(
            catalog.Resolve("456 ถนนใหม่", 1, 1001, 100102),
            Now.AddMinutes(1));

        var saved = buyer.GetSavedDeliveryAddress();
        Assert.NotNull(saved);
        Assert.Equal("456 ถนนใหม่", saved.AddressLine);
        Assert.Equal(100102, saved.SubdistrictId);
        Assert.Equal(Now.AddMinutes(1), buyer.SavedAddressUpdatedAt);
    }
}
