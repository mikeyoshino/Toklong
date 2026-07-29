using MediatR;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Toklong.Application.Abstractions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.ProcessShippingOperations;

public sealed class ShippingWorkerOptions
{
    public const string SectionName = "ShippingWorker";
    public int OperationIdleSeconds { get; init; } = 5;
    public int TrackingIntervalSeconds { get; init; } = 120;
    public int TrackingJitterSeconds { get; init; } = 30;
    public int LeaseSeconds { get; init; } = 300;
    public int MaximumAttempts { get; init; } = 8;
}

public sealed record ProcessNextShippingOperationCommand(
    string WorkerId,
    int LeaseSeconds = 300,
    int MaximumAttempts = 8) : IRequest<bool>;

public sealed class ProcessNextShippingOperationHandler(
    IShippingOperationRepository operations,
    ITransactionRepository transactions,
    IShipmentProvider provider,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions,
    ShippingOperationMetrics? metrics = null)
    : IRequestHandler<ProcessNextShippingOperationCommand, bool>
{
    public async Task<bool> Handle(
        ProcessNextShippingOperationCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var operation = await operations.ClaimDueAsync(
            request.WorkerId,
            now,
            TimeSpan.FromSeconds(
                Math.Clamp(request.LeaseSeconds, 30, 900)),
            cancellationToken);
        if (operation is null)
            return false;

        var transaction = await transactions.GetByIdAsync(
            operation.TransactionId,
            cancellationToken)
            ?? throw new InvalidOperationException(
                "shipping-transaction-missing");
        var shipment = transaction.ManagedShipments.SingleOrDefault(
            item => item.Id == operation.ManagedShipmentId)
            ?? throw new InvalidOperationException(
                "managed-shipment-missing");
        metrics?.RecordClaim(
            shipment.ServiceCode,
            now - operation.CreatedAt,
            operation.OperationType);

        try
        {
            if (!string.Equals(
                    shipment.Provider,
                    provider.ProviderName,
                    StringComparison.Ordinal))
                throw new DomainException(
                    "shipping-provider-mismatch");

            switch (operation.OperationType)
            {
                case ShippingOperationType.BookOutbound:
                case ShippingOperationType.BookReturn:
                    await BookAsync(
                        transaction,
                        shipment,
                        operation,
                        request.WorkerId,
                        cancellationToken);
                    break;
                case ShippingOperationType.ConfirmOutbound:
                case ShippingOperationType.ConfirmReturn:
                    await ConfirmAsync(
                        transaction,
                        shipment,
                        operation,
                        request.WorkerId,
                        cancellationToken);
                    break;
                case ShippingOperationType.CancelOutbound:
                case ShippingOperationType.CancelReturn:
                    await CancelAsync(
                        transaction,
                        shipment,
                        operation,
                        request.WorkerId,
                        cancellationToken);
                    break;
                default:
                    throw new DomainException(
                        "shipping-operation-unsupported");
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            metrics?.RecordSucceeded(
                shipment.ServiceCode,
                operation.OperationType);
            return true;
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (ShipmentMutationException exception)
        {
            if (exception.Outcome ==
                ShipmentMutationOutcome.OutcomeUnknown)
            {
                operation.MarkOutcomeUnknown(
                    request.WorkerId,
                    exception.SanitizedCode,
                    clock.UtcNow);
                metrics?.RecordOutcomeUnknown(
                    shipment.ServiceCode,
                    exception.SanitizedCode);
            }
            else if (operation.AttemptCount <
                     Math.Max(1, request.MaximumAttempts))
            {
                operation.ScheduleRetry(
                    request.WorkerId,
                    RetryAt(operation, clock.UtcNow),
                    exception.SanitizedCode,
                    providerReplayProvenSafe: true,
                    clock.UtcNow);
                metrics?.RecordRetry(
                    shipment.ServiceCode,
                    exception.SanitizedCode);
            }
            else
            {
                operation.SendToReview(
                    request.WorkerId,
                    "maximum-attempts-reached",
                    clock.UtcNow);
                metrics?.RecordReview(
                    shipment.ServiceCode,
                    "maximum-attempts-reached");
            }
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (HttpRequestException)
        {
            operation.MarkOutcomeUnknown(
                request.WorkerId,
                "provider-network-outcome-unknown",
                clock.UtcNow);
            metrics?.RecordOutcomeUnknown(
                shipment.ServiceCode,
                "provider-network-outcome-unknown");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DomainException)
        {
            operation.SendToReview(
                request.WorkerId,
                "provider-result-mismatch",
                clock.UtcNow);
            metrics?.RecordReview(
                shipment.ServiceCode,
                "provider-result-mismatch");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
    }

    private async Task BookAsync(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperation operation,
        string workerId,
        CancellationToken cancellationToken)
    {
        var quoteRequest = BuildQuoteRequest(
            transaction,
            shipment);
        var quote = new ShippingQuoteOption(
            shipment.Provider,
            shipment.QuoteReference,
            shipment.CarrierCode,
            shipment.ServiceCode,
            shipment.ServiceName,
            shipment.BaseShippingFeeSatang,
            shipment.InsuranceFeeSatang,
            shipment.DeclaredValueSatang,
            shipment.InsuranceCode,
            shipment.QuoteExpiresAt);
        var reservation = await provider.ReserveAsync(
            new ShipmentReservationRequest(
                transaction.Id,
                quoteRequest,
                quote),
            cancellationToken);

        if (shipment.Direction == ShipmentDirection.Return)
        {
            EnsureReservationMatches(shipment, reservation);
            shipment.RecordReservation(
                reservation.PurchaseReference,
                reservation.ProviderTrackingCode,
                reservation.CourierTrackingCode,
                reservation.ReservedAt);
        }
        else
        {
            transaction.CompleteManagedSellerAcceptance(
                shipment.Id,
                reservation.Provider,
                reservation.PurchaseReference,
                reservation.ProviderTrackingCode,
                reservation.CourierTrackingCode,
                reservation.CarrierCode,
                reservation.ServiceCode,
                reservation.FeeSatang,
                reservation.InsuranceFeeSatang,
                reservation.DeclaredValueSatang,
                reservation.InsuranceCode,
                reservation.ReservedAt,
                clock.UtcNow,
                transitions);
        }
        operation.Succeed(
            workerId,
            reservation.PurchaseReference,
            reservation.ProviderTrackingCode,
            clock.UtcNow);
    }

    private async Task ConfirmAsync(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperation operation,
        string workerId,
        CancellationToken cancellationToken)
    {
        var confirmation = await provider.ConfirmAsync(
            shipment.PurchaseReference ??
            throw new DomainException(
                "shipping-purchase-reference-missing"),
            shipment.ProviderTrackingCode ??
            throw new DomainException(
                "shipping-tracking-reference-missing"),
            shipment.CarrierCode,
            cancellationToken);
        shipment.RecordConfirmation(
            confirmation.CourierTrackingCode,
            confirmation.ProviderStatus,
            confirmation.ConfirmedAt);
        if (shipment.Direction == ShipmentDirection.Outbound)
            transaction.ConfirmProviderManagedShipment(
                shipment.Provider,
                confirmation.ProviderTrackingCode,
                confirmation.CourierTrackingCode,
                confirmation.CarrierCode,
                confirmation.ProviderStatus,
                confirmation.ConfirmedAt,
                transitions);
        operation.Succeed(
            workerId,
            shipment.PurchaseReference,
            confirmation.ProviderTrackingCode,
            clock.UtcNow);
    }

    private async Task CancelAsync(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperation operation,
        string workerId,
        CancellationToken cancellationToken)
    {
        await provider.CancelAsync(
            shipment.CourierTrackingCode ??
            shipment.ProviderTrackingCode ??
            throw new DomainException(
                "shipping-cancellation-reference-missing"),
            cancellationToken);
        shipment.RecordCancellation(clock.UtcNow);
        if (shipment.Direction == ShipmentDirection.Outbound)
            transaction.RecordShippingCancellation(
                shipment.Provider,
                clock.UtcNow);
        operation.Succeed(
            workerId,
            shipment.PurchaseReference,
            shipment.ProviderTrackingCode,
            clock.UtcNow);
    }

    private static ShippingQuoteRequest BuildQuoteRequest(
        SaleTransaction transaction,
        ManagedShipment shipment)
    {
        var origin = new ShippingContactAddress(
            transaction.SellerDisplayName,
            transaction.SellerContact,
            transaction.ShippingOriginAddressLine ??
            throw new DomainException(
                "shipping-origin-address-missing"),
            transaction.ShippingOriginSubdistrictName ?? "",
            transaction.ShippingOriginDistrictName ?? "",
            transaction.ShippingOriginProvinceName ??
            throw new DomainException(
                "shipping-origin-province-missing"),
            transaction.ShippingOriginPostalCode ??
            throw new DomainException(
                "shipping-origin-postal-code-missing"));
        var destination = new ShippingContactAddress(
            transaction.BuyerDisplayName ??
            throw new DomainException(
                "shipping-destination-name-missing"),
            transaction.BuyerContact ??
            throw new DomainException(
                "shipping-destination-contact-missing"),
            transaction.DeliveryAddressLine ??
            transaction.DeliveryAddress ??
            throw new DomainException(
                "shipping-destination-address-missing"),
            transaction.DeliverySubdistrictName ?? "",
            transaction.DeliveryDistrictName ?? "",
            transaction.DeliveryProvinceName ??
            throw new DomainException(
                "shipping-destination-province-missing"),
            transaction.DeliveryPostalCode ??
            throw new DomainException(
                "shipping-destination-postal-code-missing"));
        if (shipment.Direction == ShipmentDirection.Return)
            (origin, destination) = (destination, origin);

        return new ShippingQuoteRequest(
            origin.PostalCode,
            destination.PostalCode,
            shipment.WeightGrams,
            shipment.WidthCentimeters,
            shipment.LengthCentimeters,
            shipment.HeightCentimeters,
            origin,
            destination,
            shipment.ParcelName,
            shipment.DeclaredValueSatang);
    }

    private static DateTimeOffset RetryAt(
        ShippingOperation operation,
        DateTimeOffset now)
    {
        var exponent = Math.Min(
            Math.Max(operation.AttemptCount - 1, 0),
            6);
        var seconds = Math.Min(
            300,
            5 * (1 << exponent));
        var jitter =
            operation.Id.ToByteArray()[0] % 7;
        return now.AddSeconds(seconds + jitter);
    }

    private static void EnsureReservationMatches(
        ManagedShipment shipment,
        ShipmentReservation reservation)
    {
        if (!string.Equals(
                shipment.Provider,
                reservation.Provider,
                StringComparison.Ordinal) ||
            !string.Equals(
                shipment.CarrierCode,
                reservation.CarrierCode,
                StringComparison.Ordinal) ||
            !string.Equals(
                shipment.ServiceCode,
                reservation.ServiceCode,
                StringComparison.Ordinal) ||
            shipment.BaseShippingFeeSatang !=
                reservation.FeeSatang ||
            shipment.InsuranceFeeSatang !=
                reservation.InsuranceFeeSatang ||
            shipment.DeclaredValueSatang !=
                reservation.DeclaredValueSatang ||
            !string.Equals(
                shipment.InsuranceCode,
                reservation.InsuranceCode,
                StringComparison.Ordinal))
            throw new DomainException(
                "provider-result-mismatch");
    }
}

public sealed class ShippingOperationMetrics
{
    private readonly Meter meter =
        new("Toklong.Shipping", "1.0.0");
    private readonly Counter<long> succeeded;
    private readonly Counter<long> outcomeUnknown;
    private readonly Counter<long> retries;
    private readonly Counter<long> reviews;
    private readonly Counter<long> cancellations;
    private readonly Histogram<double> pendingAgeSeconds;

    public ShippingOperationMetrics()
    {
        succeeded = meter.CreateCounter<long>(
            "toklong.shipping.operation.succeeded");
        outcomeUnknown = meter.CreateCounter<long>(
            "toklong.shipping.operation.outcome_unknown");
        retries = meter.CreateCounter<long>(
            "toklong.shipping.operation.retry");
        reviews = meter.CreateCounter<long>(
            "toklong.shipping.case.opened");
        cancellations = meter.CreateCounter<long>(
            "toklong.shipping.cancellation.processed");
        pendingAgeSeconds = meter.CreateHistogram<double>(
            "toklong.shipping.operation.pending_age",
            "s");
    }

    public void RecordClaim(
        string serviceCode,
        TimeSpan pendingAge,
        ShippingOperationType operationType)
    {
        var tags = Tags(serviceCode, "none");
        pendingAgeSeconds.Record(
            Math.Max(0, pendingAge.TotalSeconds),
            tags);
        if (operationType is
            ShippingOperationType.CancelOutbound or
            ShippingOperationType.CancelReturn)
            cancellations.Add(1, tags);
    }

    public void RecordSucceeded(
        string serviceCode,
        ShippingOperationType operationType) =>
        succeeded.Add(1, Tags(serviceCode, "none"));

    public void RecordOutcomeUnknown(
        string serviceCode,
        string errorCode) =>
        outcomeUnknown.Add(
            1,
            Tags(serviceCode, errorCode));

    public void RecordRetry(
        string serviceCode,
        string errorCode) =>
        retries.Add(1, Tags(serviceCode, errorCode));

    public void RecordReview(
        string serviceCode,
        string errorCode) =>
        reviews.Add(1, Tags(serviceCode, errorCode));

    private static TagList Tags(
        string serviceCode,
        string errorCode) =>
        new()
        {
            {
                "service_code",
                Sanitize(serviceCode, 40)
            },
            {
                "error_code",
                Sanitize(errorCode, 100)
            }
        };

    private static string Sanitize(
        string value,
        int maximumLength)
    {
        var clean = new string(
            (value ?? "")
                .Where(character =>
                    char.IsAsciiLetterOrDigit(character) ||
                    character is '-' or '_' or '.')
                .Take(maximumLength)
                .ToArray());
        return clean.Length == 0 ? "unknown" : clean;
    }
}
