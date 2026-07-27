using System.Net.Http.Json;
using Microsoft.Maui.ApplicationModel;
using Stripe.PaymentSheet.Shared;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class StripePaymentSheetService(MobileApiClient api)
    : IStripePaymentSheetService
{
    public async Task<PaymentSheetOutcome> PresentAsync(
        Guid transactionId,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                $"api/mobile/transactions/{transactionId}/payment-sheet")
            {
                Content = JsonContent.Create(new
                {
                    AcceptedTerms = true
                })
            },
            cancellationToken);
        await MobileApiClient.EnsureSuccessAsync(response, cancellationToken);
        var preparation = await response.Content
            .ReadFromJsonAsync<PaymentSheetPreparation>(
                cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException(
                "เปิดหน้าจ่ายเงินไม่ได้");

        var paymentSheet = CreatePaymentSheet();
        paymentSheet.Init(preparation.PublishableKey);
        var result = await MainThread.InvokeOnMainThreadAsync(() =>
            paymentSheet.PresentWithPaymentIntentAsync(
                preparation.ClientSecret,
                new PaymentSheetConfiguration("TOKLONG")
                {
                    PaymentMethodOrder = ["promptpay", "card"],
                    AllowsDelayedPaymentMethods = false,
                    PrimaryButtonLabel = "จ่ายเงิน",
                    DefaultBillingDetails = new PaymentSheetBillingDetails
                    {
                        Email = preparation.ReceiptEmail
                    }
                },
                cancellationToken));
        return result.Status switch
        {
            PaymentSheetStatus.Completed => PaymentSheetOutcome.Completed,
            PaymentSheetStatus.Canceled => PaymentSheetOutcome.Cancelled,
            _ => throw new InvalidOperationException(
                result.Error?.Message ??
                "ชำระเงินไม่สำเร็จ กรุณาลองอีกครั้ง")
        };
    }

    private static IPaymentSheet CreatePaymentSheet()
    {
#if ANDROID
        return new Stripe.PaymentSheet.Android.PaymentSheet();
#elif IOS
        return new Stripe.PaymentSheet.iOS.PaymentSheet();
#else
        throw new PlatformNotSupportedException(
            "Stripe PaymentSheet ใช้ได้บน iPhone และ Android");
#endif
    }

    private sealed record PaymentSheetPreparation(
        string ClientSecret,
        string PublishableKey,
        string ReceiptEmail);
}
