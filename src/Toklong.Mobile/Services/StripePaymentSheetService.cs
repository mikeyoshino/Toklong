using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Maui.ApplicationModel;
using Stripe.PaymentSheet.Shared;
using Toklong.Mobile.Core;

namespace Toklong.Mobile.Services;

public sealed class StripePaymentSheetService(MobileApiClient api)
    : IStripePaymentSheetService
{
    public async Task<PaymentSheetOutcome> PresentAsync(
        Guid transactionId,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        using var response = await api.SendAuthenticatedAsync(
            () =>
            {
                var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    $"api/mobile/transactions/{transactionId}/payment-sheet")
                {
                    Content = JsonContent.Create(new
                    {
                        AcceptedTerms = true
                    })
                };
                request.Headers.Add(
                    "Idempotency-Key",
                    idempotencyKey);
                return request;
            },
            cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ReadPreparationErrorAsync(
                response,
                cancellationToken);
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

    private static async Task<PaymentPreparationException>
        ReadPreparationErrorAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
    {
        var code = "payment_preparation_failed";
        var detail = "เปิดหน้าจ่ายเงินไม่ได้";
        try
        {
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(
                    cancellationToken));
            var root = document.RootElement;
            if (root.TryGetProperty(
                    "code",
                    out var directCode) &&
                directCode.ValueKind ==
                    JsonValueKind.String)
                code = directCode.GetString() ?? code;
            else if (root.TryGetProperty(
                         "extensions",
                         out var extensions) &&
                     extensions.TryGetProperty(
                         "code",
                         out var extensionCode) &&
                     extensionCode.ValueKind ==
                         JsonValueKind.String)
                code =
                    extensionCode.GetString() ??
                    code;
            if (root.TryGetProperty(
                    "detail",
                    out var problemDetail) &&
                problemDetail.ValueKind ==
                    JsonValueKind.String)
                detail =
                    problemDetail.GetString() ??
                    detail;
        }
        catch (JsonException)
        {
        }

        var retryable = code is
            "shipping_retry_required" or
            "shipping_preparing" or
            "shippop-timeout" or
            "shipping-preparation-failed";
        var consumerMessage = retryable
            ? "เตรียมการจัดส่งไม่สำเร็จ\nยังไม่มีการชำระเงิน กรุณาลองอีกครั้ง"
            : detail;
        return new(
            code,
            retryable,
            consumerMessage);
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
