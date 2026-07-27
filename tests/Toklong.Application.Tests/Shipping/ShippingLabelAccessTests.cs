using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Shipping.GetShippingLabel;
using Toklong.Domain.Transactions;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Shipping;

public sealed class ShippingLabelAccessTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 26, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ConfirmedSellerReceivesProviderLabelWithLockedShipmentData()
    {
        await using var database = Database();
        var transaction = ConfirmedManagedShipment();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new CapturingProvider();
        var handler = new GetShippingLabelHandler(
            new TransactionRepository(database),
            provider);

        var html = await handler.Handle(
            new GetShippingLabelQuery(
                transaction.Id,
                transaction.SellerId!.Value),
            default);

        Assert.Equal("<html>provider label</html>", html);
        Assert.NotNull(provider.Request);
        Assert.Equal(
            transaction.TrackingNumber,
            provider.Request.TrackingNumber);
        Assert.Equal(
            TestTransactionFactory.DeliveryAddressLine,
            provider.Request.Destination.AddressLine);
        Assert.Equal(
            "ผู้ขาย ทดสอบ",
            provider.Request.Origin.Name);
        Assert.Equal(1_200, provider.Request.WeightGrams);
    }

    [Fact]
    public async Task NonSellerCannotFetchPrivateShippingLabel()
    {
        await using var database = Database();
        var transaction = ConfirmedManagedShipment();
        database.Transactions.Add(transaction);
        await database.SaveChangesAsync();
        var provider = new CapturingProvider();
        var handler = new GetShippingLabelHandler(
            new TransactionRepository(database),
            provider);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            handler.Handle(
                new GetShippingLabelQuery(
                    transaction.Id,
                    Guid.NewGuid()),
                default));
        Assert.Null(provider.Request);
    }

    [Fact]
    public async Task DevelopmentLabelContainsScannableBarcodeAndNoCodClaim()
    {
        var provider =
            new Infrastructure.Services
                .DevelopmentShippingQuoteProvider(
                    new FixedClock());
        var request = new ShipmentLabelRequest(
            "dev-purchase",
            "FLASH",
            "Flash Express Standard",
            "TH260756219853",
            Address("ผู้ขาย ทดสอบ", "+66811111111"),
            Address("ผู้ซื้อ ทดสอบ", "+66822222222"),
            1_200);

        var html = await provider.GetLabelHtmlAsync(
            request,
            default);

        Assert.Contains("<svg", html);
        Assert.Contains("TH260756219853", html);
        Assert.Contains("ชำระแล้ว", html);
        Assert.DoesNotContain(
            "เก็บเงินปลายทาง",
            html,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            ">COD<",
            html,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "<script",
            html,
            StringComparison.OrdinalIgnoreCase);
    }

    private static SaleTransaction ConfirmedManagedShipment()
    {
        var transitions =
            new TransactionTransitionService();
        var transaction =
            TestTransactionFactory.CreateBuyerOffer(
                Guid.NewGuid(),
                "ผู้ซื้อ ทดสอบ",
                "+66822222222",
                "+66811111111",
                FulfillmentType.PhysicalShipment,
                "กล้องทดสอบ",
                "กล้องพร้อมเลนส์",
                ConditionCode.UsedGood,
                "ไม่มีตำหนิที่ผู้ซื้อระบุ",
                null,
                120_000,
                "terms-v1",
                Now,
                transitions);
        var sellerId = Guid.NewGuid();
        transaction.AcceptBuyerOffer(
            sellerId,
            "ผู้ขาย ทดสอบ",
            "+66811111111",
            "KBANK",
            "ผู้ขาย ทดสอบ",
            "1234567890",
            true,
            Now.AddMinutes(1),
            transitions,
            shipping: new AcceptedShippingQuote(
                TestTransactionFactory.ShippingOriginAddress,
                TestTransactionFactory.DeliveryProvinceName,
                TestTransactionFactory.DeliveryPostalCode,
                1_200,
                20,
                30,
                15,
                "test-provider",
                "quote-1",
                "FLASH",
                "STANDARD",
                "Flash Express Standard",
                5_000,
                Now.AddHours(2),
                TestTransactionFactory.DeliveryDistrictName,
                TestTransactionFactory.DeliverySubdistrictName,
                "purchase-1",
                "provider-track-1",
                null,
                Now.AddMinutes(1),
                "99/9 ถนนสุขุมวิท"));
        transaction.BeginCheckout(
            "ผู้ซื้อ ทดสอบ",
            "+66822222222",
            TestTransactionFactory.DeliveryAddress,
            Now.AddMinutes(2),
            transitions);
        transaction.ConfirmPayment(
            "payment-1",
            Now.AddMinutes(3),
            transitions);
        transaction.ConfirmProviderManagedShipment(
            "test-provider",
            "provider-track-1",
            "TH260756219853",
            "FLASH",
            "booking",
            Now.AddMinutes(4),
            transitions);
        return transaction;
    }

    private static ShippingContactAddress Address(
        string name,
        string phone) =>
        new(
            name,
            phone,
            "99/9 ถนนสุขุมวิท",
            "คลองเตยเหนือ",
            "วัฒนา",
            "กรุงเทพมหานคร",
            "10110");

    private static ToklongDbContext Database()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString("N"))
                .Options;
        return new ToklongDbContext(options);
    }

    private sealed class CapturingProvider :
        IShipmentProvider
    {
        public string ProviderName => "test-provider";

        public ShipmentLabelRequest? Request { get; private set; }

        public Task<string> GetLabelHtmlAsync(
            ShipmentLabelRequest request,
            CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(
                "<html>provider label</html>");
        }

        public Task<ShipmentReservation> ReserveAsync(
            ShipmentReservationRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentTrackingUpdate> GetTrackingAsync(
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ShipmentConfirmation> ConfirmAsync(
            string purchaseReference,
            string providerTrackingCode,
            string carrierCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task CancelAsync(
            string courierTrackingCode,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
