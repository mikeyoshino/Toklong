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
    public async Task Both_parties_can_download_same_hashed_evidence()
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

        Assert.Equal(buyerFile.EvidenceHash, sellerFile.EvidenceHash);
        Assert.Equal(buyerFile.JsonBytes, sellerFile.JsonBytes);
        Assert.Contains(
            "การยอมรับข้อตกลงทางอิเล็กทรอนิกส์",
            System.Text.Encoding.UTF8.GetString(
                buyerFile.HtmlBytes));
        using var json = JsonDocument.Parse(
            buyerFile.JsonBytes);
        Assert.Equal(
            buyerFile.EvidenceHash,
            json.RootElement
                .GetProperty("evidenceHashSha256")
                .GetString());
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
            shipping: TestTransactionFactory.ShippingQuote(
                Now.AddMinutes(1)));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66811111111",
            "123 ถนนสุขุมวิท กรุงเทพมหานคร 10110",
            Now.AddMinutes(2),
            transitions);
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
