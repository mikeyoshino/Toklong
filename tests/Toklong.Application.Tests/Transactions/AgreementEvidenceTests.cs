using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Toklong.Application.Common;
using Toklong.Application.Features.Transactions.GetAgreementEvidence;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Transactions;

public sealed class AgreementEvidenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 25, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Evidence_is_shaped_for_each_party_without_leaking_buyer_protection_to_seller()
    {
        await using var db = CreateDatabase();
        var transaction = CreateAcceptedAgreement();
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var handler = new GetAgreementEvidenceHandler(
            new TransactionRepository(db));

        var buyerFile = await handler.Handle(
            new GetAgreementEvidenceQuery(
                transaction.Id,
                transaction.BuyerId,
                null),
            default);
        var sellerFile = await handler.Handle(
            new GetAgreementEvidenceQuery(
                transaction.Id,
                null,
                transaction.SellerId),
            default);

        Assert.NotEqual(buyerFile.EvidenceHash, sellerFile.EvidenceHash);
        var buyerHtml = System.Text.Encoding.UTF8.GetString(
            buyerFile.HtmlBytes);
        Assert.Contains(
            "การยอมรับข้อตกลงทางอิเล็กทรอนิกส์",
            buyerHtml);
        Assert.Contains("ค่าคุ้มครองผู้ซื้อ", buyerHtml);
        Assert.Contains("ยอดรวม", buyerHtml);
        using var buyerJson = JsonDocument.Parse(
            buyerFile.JsonBytes);
        using var sellerJson = JsonDocument.Parse(
            sellerFile.JsonBytes);
        Assert.Equal(
            buyerFile.EvidenceHash,
            buyerJson.RootElement
                .GetProperty("evidenceHashSha256")
                .GetString());
        var buyerAmount = buyerJson.RootElement
            .GetProperty("evidence")
            .GetProperty("amount");
        Assert.Equal(
            transaction.BuyerTotalSatang,
            buyerAmount.GetProperty("buyerTotalSatang").GetInt64());
        Assert.Equal(
            transaction.BuyerProtectionFeeSatang,
            buyerAmount
                .GetProperty("buyerProtectionFeeSatang")
                .GetInt64());
        Assert.Equal(
            "buyer-protection-v2",
            buyerJson.RootElement
                .GetProperty("evidence")
                .GetProperty("terms")
                .GetProperty("feePolicyVersion")
                .GetString());

        var sellerEvidence = sellerJson.RootElement
            .GetProperty("evidence");
        var sellerAmount = sellerEvidence.GetProperty("amount");
        Assert.False(sellerAmount.TryGetProperty(
            "buyerTotalSatang", out _));
        Assert.False(sellerAmount.TryGetProperty(
            "buyerProtectionFeeSatang", out _));
        Assert.False(sellerAmount.TryGetProperty(
            "platformFeeSatang", out _));
        Assert.False(sellerEvidence.GetProperty("terms").TryGetProperty(
            "feePolicyVersion", out _));
        var sellerJsonText = System.Text.Encoding.UTF8.GetString(
            sellerFile.JsonBytes);
        Assert.DoesNotContain("buyerTotalSatang", sellerJsonText);
        Assert.DoesNotContain("buyerProtectionFeeSatang", sellerJsonText);
        Assert.DoesNotContain("feePolicyVersion", sellerJsonText);
        Assert.DoesNotContain("insurance", sellerJsonText,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("quoteReference", sellerJsonText,
            StringComparison.OrdinalIgnoreCase);

        var sellerHtml = System.Text.Encoding.UTF8.GetString(
            sellerFile.HtmlBytes);
        Assert.DoesNotContain("ค่าคุ้มครองผู้ซื้อ", sellerHtml);
        Assert.DoesNotContain("ยอดรวม", sellerHtml);
        Assert.DoesNotContain(
            "otp",
            System.Text.Encoding.UTF8.GetString(
                buyerFile.JsonBytes),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Non_party_cannot_download_agreement_evidence()
    {
        await using var db = CreateDatabase();
        var transaction = CreateAcceptedAgreement();
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var handler = new GetAgreementEvidenceHandler(
            new TransactionRepository(db));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new GetAgreementEvidenceQuery(
                    transaction.Id,
                    Guid.NewGuid(),
                    null),
                default));
    }

    [Fact]
    public async Task Evidence_represents_an_omitted_product_photo_as_null()
    {
        await using var db = CreateDatabase();
        var transaction = CreateAcceptedAgreement(null);
        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();
        var handler = new GetAgreementEvidenceHandler(
            new TransactionRepository(db));

        var file = await handler.Handle(
            new GetAgreementEvidenceQuery(
                transaction.Id,
                transaction.BuyerId,
                null),
            default);

        using var json = JsonDocument.Parse(file.JsonBytes);
        Assert.Equal(
            JsonValueKind.Null,
            json.RootElement
                .GetProperty("evidence")
                .GetProperty("item")
                .GetProperty("photoUrl")
                .ValueKind);
    }

    private static SaleTransaction CreateAcceptedAgreement(
        string? photoUrl = "https://example.com/photo.jpg")
    {
        var transitions =
            new TransactionTransitionService();
        var transaction =
            TestTransactionFactory.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ ทดสอบ",
                "+66811111111",
                "+66822222222",
                FulfillmentType.PhysicalShipment,
                "กล้องพร้อมเลนส์",
                "ใช้งานได้ปกติ มีรอยตามรูป",
                ConditionCode.UsedDefects,
                "มีรอยด้านข้าง",
                photoUrl,
                450_000,
                "mvp-th-2026-07",
                Now,
                transitions);
        transaction.AcceptBuyerOffer(
            Guid.NewGuid(),
            "ผู้ขาย ทดสอบ",
            "+66822222222",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddMinutes(1),
            transitions,
            buyerProtectionFeeSatang: 5_900,
            platformFeeSatang: 10_000,
            sellerExpectedNetSatang: 440_000,
            feePolicyVersion: "buyer-protection-v2",
            shipping: TestTransactionFactory.ShippingQuote(
                Now.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            Now.AddMinutes(2),
            transitions,
            platformFeeSatang: 10_000,
            sellerExpectedNetSatang: 440_000,
            feePolicyVersion: "buyer-protection-v2",
            buyerProtectionFeeSatang: 5_900);
        return transaction;
    }

    private static ToklongDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;
        return new ToklongDbContext(options);
    }
}
