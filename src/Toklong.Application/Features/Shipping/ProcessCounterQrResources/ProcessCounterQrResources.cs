using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.ProcessCounterQrResources;

public sealed record ProcessNextCounterQrResourceCommand(
    string WorkerId,
    int LeaseSeconds = 300,
    int MaximumAttempts = 8) : IRequest<bool>;

public sealed record ProcessCounterQrResourceBatchCommand(
    string WorkerId,
    int BatchSize = 20,
    int LeaseSeconds = 300,
    int MaximumAttempts = 8) : IRequest<int>;

public sealed record QueueEligibleCounterQrResourcesCommand :
    IRequest<int>;

public sealed class QueueEligibleCounterQrResourcesHandler(
    ITransactionRepository transactions,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<
        QueueEligibleCounterQrResourcesCommand,
        int>
{
    public async Task<int> Handle(
        QueueEligibleCounterQrResourcesCommand request,
        CancellationToken cancellationToken)
    {
        var eligible = await transactions
            .GetEligibleCounterQrTransactionsAsync(
                cancellationToken);
        var queued = 0;
        foreach (var transaction in eligible)
        {
            var shipment = transaction.ManagedShipments
                .Where(item =>
                    item.Direction == ShipmentDirection.Outbound &&
                    item.ConfirmedAt.HasValue &&
                    !item.CancelledAt.HasValue &&
                    item.CounterQrResource is null)
                .OrderByDescending(item => item.CreatedAt)
                .FirstOrDefault();
            if (shipment is null)
                continue;
            if (!transaction.IsCounterQrAccessAllowed(shipment))
                continue;
            transaction.QueueShipmentCounterQr(
                shipment.Id,
                "counter-qr-queue-worker",
                clock.UtcNow);
            queued++;
        }
        if (queued > 0)
            await unitOfWork.SaveChangesAsync(cancellationToken);
        return queued;
    }
}

public sealed class ProcessCounterQrResourceBatchHandler(
    ISender sender) : IRequestHandler<
        ProcessCounterQrResourceBatchCommand,
        int>
{
    public async Task<int> Handle(
        ProcessCounterQrResourceBatchCommand request,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        var limit = Math.Clamp(request.BatchSize, 1, 100);
        for (; processed < limit; processed++)
        {
            if (!await sender.Send(
                    new ProcessNextCounterQrResourceCommand(
                        request.WorkerId,
                        request.LeaseSeconds,
                        request.MaximumAttempts),
                    cancellationToken))
                break;
        }
        return processed;
    }
}

public sealed class ProcessNextCounterQrResourceHandler(
    ICounterQrResourceRepository resources,
    IShipmentProvider provider,
    ICounterQrArtifactProtector artifactProtector,
    IUnitOfWork unitOfWork,
    IClock clock) : IRequestHandler<
        ProcessNextCounterQrResourceCommand,
        bool>
{
    public async Task<bool> Handle(
        ProcessNextCounterQrResourceCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var resource = await resources.ClaimDueAsync(
            request.WorkerId,
            now,
            TimeSpan.FromSeconds(
                Math.Clamp(request.LeaseSeconds, 30, 900)),
            cancellationToken);
        if (resource is null)
            return false;

        var transaction = await resources.GetTransactionAsync(
            resource.TransactionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "counter-qr-transaction-missing");
        var shipment = transaction.ManagedShipments
            .SingleOrDefault(item =>
                item.Id == resource.ManagedShipmentId &&
                item.CounterQrResource?.Id == resource.Id)
            ?? throw new InvalidOperationException(
                "counter-qr-shipment-missing");

        if (!transaction.IsCounterQrAccessAllowed(shipment) ||
            !string.Equals(
                shipment.Provider,
                provider.ProviderName,
                StringComparison.Ordinal))
        {
            resource.RecordUnavailable(
                "counter-qr-ineligible",
                now,
                request.WorkerId);
            transaction.RecordShipmentCounterQrOutcome(
                resource.Id,
                "unavailable",
                "counter-qr-ineligible",
                request.WorkerId,
                now);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }

        try
        {
            var result = await provider.GetCounterQrAsync(
                new CounterQrRequest(
                    transaction.Id,
                    shipment.Id,
                    shipment.Provider,
                    shipment.PurchaseReference ?? "",
                    shipment.ProviderTrackingCode ?? "",
                    shipment.CourierTrackingCode ?? "",
                    shipment.CarrierCode,
                    shipment.ServiceCode),
                cancellationToken);
            ApplyResult(
                transaction,
                resource,
                result,
                request.WorkerId,
                Math.Clamp(request.MaximumAttempts, 1, 8));
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            RecordRetry(
                transaction,
                resource,
                request.WorkerId,
                "counter-qr-network",
                Math.Clamp(request.MaximumAttempts, 1, 8));
        }
        catch
        {
            RecordRetry(
                transaction,
                resource,
                request.WorkerId,
                "counter-qr-provider-error",
                Math.Clamp(request.MaximumAttempts, 1, 8));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void ApplyResult(
        SaleTransaction transaction,
        ShipmentCounterQrResource resource,
        CounterQrReadResult result,
        string workerId,
        int maximumAttempts)
    {
        switch (result.Status)
        {
            case CounterQrReadStatus.Ready:
                if (result.Representation !=
                        CounterQrRepresentation.ProviderPng ||
                    result.Artifact is null)
                    throw new InvalidOperationException(
                        "counter-qr-provider-result-invalid");
                var protectedArtifact = artifactProtector.Protect(
                    new CounterQrArtifact(
                        result.Artifact,
                        "image/png"));
                resource.RecordReady(
                    result.Representation.Value,
                    protectedArtifact.Ciphertext,
                    protectedArtifact.ProtectionVersion,
                    protectedArtifact.Sha256,
                    result.ProviderResourceDigest,
                    result.ExpiresAt,
                    result.FetchedAt,
                    workerId);
                transaction.RecordShipmentCounterQrOutcome(
                    resource.Id,
                    "ready",
                    null,
                    workerId,
                    result.FetchedAt);
                break;
            case CounterQrReadStatus.RetryableError:
                RecordRetry(
                    transaction,
                    resource,
                    workerId,
                    result.SanitizedErrorCode ??
                        "counter-qr-retryable",
                    maximumAttempts);
                break;
            case CounterQrReadStatus.Unavailable:
                resource.RecordUnavailable(
                    result.SanitizedErrorCode ??
                        "counter-qr-unavailable",
                    result.FetchedAt,
                    workerId);
                transaction.RecordShipmentCounterQrOutcome(
                    resource.Id,
                    "unavailable",
                    result.SanitizedErrorCode ??
                        "counter-qr-unavailable",
                    workerId,
                    result.FetchedAt);
                break;
            default:
                throw new InvalidOperationException(
                    "counter-qr-provider-status-invalid");
        }
    }

    private void RecordRetry(
        SaleTransaction transaction,
        ShipmentCounterQrResource resource,
        string workerId,
        string safeCode,
        int maximumAttempts)
    {
        var now = clock.UtcNow;
        if (resource.AttemptCount >= maximumAttempts)
        {
            resource.RecordUnavailable(
                "counter-qr-maximum-attempts",
                now,
                workerId);
            transaction.RecordShipmentCounterQrOutcome(
                resource.Id,
                "unavailable",
                "counter-qr-maximum-attempts",
                workerId,
                now);
            return;
        }
        resource.RecordRetryableError(
            safeCode,
            RetryAt(resource, now),
            now,
            workerId);
        transaction.RecordShipmentCounterQrOutcome(
            resource.Id,
            "retryable_error",
            safeCode,
            workerId,
            now);
    }

    internal static DateTimeOffset RetryAt(
        ShipmentCounterQrResource resource,
        DateTimeOffset now)
    {
        var exponent = Math.Clamp(
            resource.AttemptCount - 1,
            0,
            6);
        var baseSeconds = Math.Min(300, 5 * (1 << exponent));
        var jitter = resource.Id.ToByteArray()[0] % 6;
        return now.AddSeconds(baseSeconds + jitter);
    }
}
