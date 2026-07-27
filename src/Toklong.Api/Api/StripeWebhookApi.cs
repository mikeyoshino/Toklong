using MediatR;
using Stripe;
using Toklong.Application.Common;
using Toklong.Application.Features.ExternalEvents;
using Toklong.Api.Security;
using Toklong.Domain.Common;
using Toklong.Infrastructure.Payments;

namespace Toklong.Api.Api;

public static class StripeWebhookApi
{
    public static void MapStripeWebhook(this WebApplication app)
    {
        app.MapPost("/api/webhooks/stripe", HandleAsync)
            .DisableAntiforgery();
    }

    private static async Task<IResult> HandleAsync(
        HttpRequest request,
        StripePaymentOptions options,
        StripeWebhookEventParser parser,
        ISender sender,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!options.Enabled ||
            string.IsNullOrWhiteSpace(options.WebhookSecret))
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Stripe webhook ยังไม่ได้ตั้งค่า");
        if (!request.Headers.TryGetValue(
                "Stripe-Signature",
                out var signature))
            return Results.Unauthorized();

        using var reader = new StreamReader(request.Body);
        var payload = await reader.ReadToEndAsync(cancellationToken);
        Event stripeEvent;
        try
        {
            stripeEvent = parser.Parse(
                payload,
                signature.ToString(),
                options.WebhookSecret);
        }
        catch (StripeException)
        {
            return Results.BadRequest();
        }

        if (stripeEvent.Livemode != options.LiveMode)
            return Results.BadRequest(
                new { error = "Stripe event environment ไม่ตรงกับระบบ" });
        if (stripeEvent.Type == EventTypes.RefundUpdated)
        {
            if (stripeEvent.Data.Object is not Refund refund)
                return Results.BadRequest(
                    new { error = "Stripe refund event ไม่ถูกต้อง" });
            var refundStatus =
                refund.Status?.Trim().ToLowerInvariant();
            if (refundStatus is not (
                    "succeeded" or
                    "requires_action" or
                    "pending"))
                return Results.Ok(new { received = true });
            return await HandleRefundAsync(
                stripeEvent,
                refund,
                refundStatus,
                sender,
                loggerFactory,
                cancellationToken);
        }
        if (stripeEvent.Type != EventTypes.PaymentIntentSucceeded)
            return Results.Ok(new { received = true });
        if (stripeEvent.Data.Object is not PaymentIntent intent ||
            intent.Livemode != options.LiveMode ||
            !string.Equals(
                intent.Status,
                "succeeded",
                StringComparison.OrdinalIgnoreCase))
            return Results.BadRequest(
                new { error = "Stripe payment event ไม่ถูกต้อง" });
        if (!intent.Metadata.TryGetValue(
                "toklong_transaction_id",
                out var rawTransactionId) ||
            !Guid.TryParse(rawTransactionId, out var transactionId))
            return Results.BadRequest(
                new { error = "Stripe event ไม่มี transaction id" });

        ExternalEventResult result;
        try
        {
            result = await sender.Send(
                new ConfirmStripePaymentCommand(
                    transactionId,
                    stripeEvent.Id,
                    intent.Id,
                    intent.Amount,
                    intent.Currency,
                    stripeEvent.Created),
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is DomainException or NotFoundException)
        {
            loggerFactory
                .CreateLogger("Toklong.StripeWebhook")
                .LogWarning(
                    exception,
                    "Rejected Stripe payment event {StripeEventId}",
                    stripeEvent.Id);
            return Results.BadRequest(
                new { error = "Stripe payment event ไม่ตรงกับรายการ" });
        }
        return Results.Ok(new
        {
            received = true,
            result.AlreadyProcessed,
            state = result.Transaction.State.ToString()
        });
    }

    private static async Task<IResult> HandleRefundAsync(
        Event stripeEvent,
        Refund refund,
        string refundStatus,
        ISender sender,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!refund.Metadata.TryGetValue(
                "toklong_transaction_id",
                out var rawTransactionId) ||
            !Guid.TryParse(rawTransactionId, out var transactionId) ||
            string.IsNullOrWhiteSpace(refund.PaymentIntentId))
            return Results.BadRequest(
                new { error = "Stripe refund ไม่มีข้อมูลรายการ" });
        ExternalEventResult result;
        try
        {
            result = refundStatus == "succeeded"
                ? await sender.Send(
                    new ConfirmStripeRefundCommand(
                        transactionId,
                        stripeEvent.Id,
                        refund.Id,
                        refund.PaymentIntentId,
                        refund.Amount,
                        refund.Currency,
                        stripeEvent.Created),
                    cancellationToken)
                : await sender.Send(
                    new RecordStripeRefundProgressCommand(
                        transactionId,
                        stripeEvent.Id,
                        refund.Id,
                        refund.PaymentIntentId,
                        refund.Amount,
                        refund.Currency,
                        refundStatus,
                        stripeEvent.Created,
                        refund.NextAction?
                            .DisplayDetails?
                            .ExpiresAt,
                        refund.NextAction?
                            .DisplayDetails?
                            .EmailSent?
                            .EmailSentAt),
                    cancellationToken);
        }
        catch (Exception exception) when (
            exception is DomainException or NotFoundException)
        {
            loggerFactory
                .CreateLogger("Toklong.StripeWebhook")
                .LogWarning(
                    exception,
                    "Rejected Stripe refund event {StripeEventId}",
                    stripeEvent.Id);
            return Results.BadRequest(
                new { error = "Stripe refund event ไม่ตรงกับรายการ" });
        }
        return Results.Ok(new
        {
            received = true,
            result.AlreadyProcessed,
            state = result.Transaction.State.ToString()
        });
    }
}
