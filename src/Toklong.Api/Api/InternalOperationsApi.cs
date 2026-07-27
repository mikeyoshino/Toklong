using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.ExternalEvents;
using Toklong.Application.Features.Retention.ExecuteRetention;
using Toklong.Application.Features.Retention.ManageLegalHold;
using Toklong.Domain.Transactions;

namespace Toklong.Api.Api;

public static class InternalOperationsApi
{
    public static void MapInternalOperationsApi(
        this WebApplication app)
    {
        app.MapPost(
                "/api/internal/transactions/{transactionId:guid}/carrier-events",
                RecordCarrierEventAsync)
            .DisableAntiforgery();
        app.MapPost(
                "/api/internal/transactions/{transactionId:guid}/legal-hold",
                PlaceLegalHoldAsync)
            .DisableAntiforgery();
        app.MapPost(
                "/api/internal/transactions/{transactionId:guid}/legal-hold/release",
                ReleaseLegalHoldAsync)
            .DisableAntiforgery();
        app.MapPost(
                "/api/internal/retention/preview",
                PreviewRetentionAsync)
            .DisableAntiforgery();
    }

    private static async Task<IResult> RecordCarrierEventAsync(
        Guid transactionId,
        CarrierReconciliationRequest request,
        HttpRequest httpRequest,
        IWebhookSignatureVerifier signatures,
        ISender sender,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue(
                "X-Toklong-Signature",
                out var signature))
            return Results.Unauthorized();

        var now = timeProvider.GetUtcNow();
        if (request.RequestedAt < now.AddMinutes(-5) ||
            request.RequestedAt > now.AddMinutes(1))
            return Results.Unauthorized();

        var eventType = request.EventType.Trim().ToLowerInvariant();
        var carrierCode =
            request.CarrierCode.Trim().ToUpperInvariant();
        var tracking = SupportedCarrierCatalog.NormalizeTracking(
            request.TrackingNumber);
        var eventId = request.EventId.Trim();
        var payload =
            $"carrier|{transactionId:N}|{eventId}|" +
            $"{eventType}|{carrierCode}|{tracking}|" +
            $"{request.RequestedAt.ToUnixTimeSeconds()}";
        if (!signatures.Verify(payload, signature.ToString()))
            return Results.Unauthorized();

        if (eventType is not ("in_transit" or "delivered" or "unverified"))
            return Results.BadRequest(
                new { message = "ไม่รองรับสถานะขนส่งนี้" });
        var carrier = SupportedCarrierCatalog.Find(carrierCode);
        if (carrier is null ||
            !carrier.IsValidTrackingNumber(tracking))
            return Results.BadRequest(
                new
                {
                    message = carrier?.ValidationMessage ??
                              "ไม่รองรับบริษัทขนส่งนี้"
                });
        if (eventId.Length is < 8 or > 100)
            return Results.BadRequest(
                new { message = "event id ไม่ถูกต้อง" });

        var result = await sender.Send(
            new RecordCarrierEventCommand(
                transactionId,
                eventId,
                eventType,
                request.OccurredAt,
                carrier.Code,
                tracking),
            cancellationToken);
        return Results.Ok(
            new
            {
                result.AlreadyProcessed,
                State = result.Transaction.State.ToString()
            });
    }

    private static async Task<IResult> PlaceLegalHoldAsync(
        Guid transactionId,
        PlaceLegalHoldRequest request,
        HttpRequest httpRequest,
        IWebhookSignatureVerifier signatures,
        ISender sender,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TrySafeSegment(
                request.Reference,
                160,
                out var reference) ||
            !TrySafeSegment(
                request.Reason,
                500,
                out var reason))
            return Results.BadRequest(
                new { message = "ข้อมูล legal hold ไม่ถูกต้อง" });
        var payload =
            $"legal-hold|place|{transactionId:N}|" +
            $"{reference}|{reason}|" +
            $"{request.RequestedAt.ToUnixTimeSeconds()}";
        if (!IsAuthorized(
                httpRequest,
                signatures,
                timeProvider,
                request.RequestedAt,
                payload))
            return Results.Unauthorized();
        await sender.Send(
            new PlaceLegalHoldCommand(
                transactionId,
                reference,
                reason),
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ReleaseLegalHoldAsync(
        Guid transactionId,
        ReleaseLegalHoldRequest request,
        HttpRequest httpRequest,
        IWebhookSignatureVerifier signatures,
        ISender sender,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!TrySafeSegment(
                request.Reference,
                160,
                out var reference))
            return Results.BadRequest(
                new { message = "ข้อมูล legal hold ไม่ถูกต้อง" });
        var payload =
            $"legal-hold|release|{transactionId:N}|" +
            $"{reference}|" +
            $"{request.RequestedAt.ToUnixTimeSeconds()}";
        if (!IsAuthorized(
                httpRequest,
                signatures,
                timeProvider,
                request.RequestedAt,
                payload))
            return Results.Unauthorized();
        await sender.Send(
            new ReleaseLegalHoldCommand(
                transactionId,
                reference),
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> PreviewRetentionAsync(
        RetentionOperationRequest request,
        HttpRequest httpRequest,
        IWebhookSignatureVerifier signatures,
        ISender sender,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var payload =
            $"retention|preview|{request.BatchSize}|" +
            $"{request.RequestedAt.ToUnixTimeSeconds()}";
        if (!IsAuthorized(
                httpRequest,
                signatures,
                timeProvider,
                request.RequestedAt,
                payload))
            return Results.Unauthorized();
        return Results.Ok(await sender.Send(
            new PreviewRetentionQuery(
                request.BatchSize),
            cancellationToken));
    }

    private static bool IsAuthorized(
        HttpRequest request,
        IWebhookSignatureVerifier signatures,
        TimeProvider timeProvider,
        DateTimeOffset requestedAt,
        string payload)
    {
        if (!request.Headers.TryGetValue(
                "X-Toklong-Signature",
                out var signature))
            return false;
        var now = timeProvider.GetUtcNow();
        return requestedAt >= now.AddMinutes(-5) &&
               requestedAt <= now.AddMinutes(1) &&
               signatures.Verify(
                   payload,
                   signature.ToString());
    }

    private static bool TrySafeSegment(
        string? value,
        int maximumLength,
        out string clean)
    {
        clean = value?.Trim() ?? "";
        return clean.Length is > 0 &&
               clean.Length <= maximumLength &&
               !clean.Contains('|');
    }
}

public sealed record CarrierReconciliationRequest(
    string EventId,
    string EventType,
    DateTimeOffset OccurredAt,
    DateTimeOffset RequestedAt,
    string CarrierCode,
    string TrackingNumber);

public sealed record PlaceLegalHoldRequest(
    string? Reference,
    string? Reason,
    DateTimeOffset RequestedAt);

public sealed record ReleaseLegalHoldRequest(
    string? Reference,
    DateTimeOffset RequestedAt);

public sealed record RetentionOperationRequest(
    int BatchSize,
    DateTimeOffset RequestedAt);
