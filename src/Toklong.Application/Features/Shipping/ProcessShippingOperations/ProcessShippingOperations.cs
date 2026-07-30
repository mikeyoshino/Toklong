using MediatR;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Checkout.GetParcelProtection;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Shipping.ProcessShippingOperations;

public sealed class ShippingWorkerOptions
{
    public const string SectionName = "ShippingWorker";
    public int OperationIdleSeconds { get; init; } = 5;
    public int ConfirmationBatchSize { get; init; } = 50;
    public int OtherMutationBatchSize { get; init; } = 20;
    public int TrackingIntervalSeconds { get; init; } = 120;
    public int TrackingJitterSeconds { get; init; } = 30;
    public int LeaseSeconds { get; init; } = 300;
    public int MaximumAttempts { get; init; } = 8;
}

public sealed record ProcessNextShippingOperationCommand(
    string WorkerId,
    int LeaseSeconds = 300,
    int MaximumAttempts = 8,
    IReadOnlySet<ShippingOperationType>?
        AllowedTypes = null) : IRequest<bool>;

public sealed record ProcessShippingOperationBatchCommand(
    string WorkerId,
    IReadOnlySet<ShippingOperationType> AllowedTypes,
    int BatchSize,
    int LeaseSeconds = 300,
    int MaximumAttempts = 8) : IRequest<int>;

public sealed class ProcessShippingOperationBatchHandler(
    ISender sender)
    : IRequestHandler<
        ProcessShippingOperationBatchCommand,
        int>
{
    public async Task<int> Handle(
        ProcessShippingOperationBatchCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            request.AllowedTypes);
        if (request.AllowedTypes.Count == 0)
            throw new ArgumentException(
                "At least one shipping operation type is required.");
        var processed = 0;
        var limit = Math.Clamp(
            request.BatchSize,
            1,
            100);
        for (; processed < limit; processed++)
        {
            var hadWork = await sender.Send(
                new ProcessNextShippingOperationCommand(
                    request.WorkerId,
                    request.LeaseSeconds,
                    request.MaximumAttempts,
                    request.AllowedTypes),
                cancellationToken);
            if (!hadWork)
                break;
        }
        return processed;
    }
}

public sealed class ProcessNextShippingOperationHandler(
    IShippingOperationRepository operations,
    ITransactionRepository transactions,
    IShipmentProvider provider,
    IParcelProtectionQuoteProvider parcelProtectionQuotes,
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
        var allowedTypes =
            request.AllowedTypes ??
            Enum.GetValues<
                    ShippingOperationType>()
                .ToHashSet();
        var operation = await operations.ClaimDueAsync(
            request.WorkerId,
            now,
            TimeSpan.FromSeconds(
                Math.Clamp(request.LeaseSeconds, 30, 900)),
            allowedTypes,
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
            if (!string.Equals(
                    operation.RequestFingerprint,
                    CurrentFingerprint(
                        transaction,
                        shipment,
                        operation.OperationType),
                    StringComparison.Ordinal))
                throw new DomainException(
                    "shipping-request-fingerprint-mismatch");

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
            RecordParcelProtectionBookingOutcome(
                transaction,
                shipment,
                operation,
                operation.Status.ToString(),
                exception.SanitizedCode,
                clock.UtcNow);
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
            RecordParcelProtectionBookingOutcome(
                transaction,
                shipment,
                operation,
                operation.Status.ToString(),
                "provider-network-outcome-unknown",
                clock.UtcNow);
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
            RecordParcelProtectionBookingOutcome(
                transaction,
                shipment,
                operation,
                operation.Status.ToString(),
                "provider-result-mismatch",
                clock.UtcNow);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch
        {
            // A worker must never leave a claimed mutation in Processing.
            // Unknown adapter failures require an operator decision and must
            // not be converted into an automatic replay.
            operation.SendToReview(
                request.WorkerId,
                "unexpected-provider-failure",
                clock.UtcNow);
            metrics?.RecordReview(
                shipment.ServiceCode,
                "unexpected-provider-failure");
            RecordParcelProtectionBookingOutcome(
                transaction,
                shipment,
                operation,
                operation.Status.ToString(),
                "unexpected-provider-failure",
                clock.UtcNow);
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
        if (shipment.QuoteExpiresAt <= clock.UtcNow)
            throw new DomainException(
                "shipping-quote-expired");
        if (shipment.Direction == ShipmentDirection.Outbound &&
            !await RevalidateOutboundParcelProtectionAsync(
                transaction,
                shipment,
                operation,
                workerId,
                cancellationToken))
            return;
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
                quote,
                shipment.Id,
                shipment.Direction ==
                    ShipmentDirection.Return,
                operation.Id.ToString("N")),
            cancellationToken);

        if (shipment.Direction == ShipmentDirection.Return)
        {
            EnsureReservationMatches(shipment, reservation);
            shipment.RecordReservation(
                reservation.PurchaseReference,
                reservation.ProviderTrackingCode,
                reservation.CourierTrackingCode,
                reservation.ReservedAt);
            transaction.RecordManagedReturnCost(
                shipment.Id,
                reservation.Provider,
                reservation.PurchaseReference,
                checked(
                    reservation.FeeSatang +
                    reservation.InsuranceFeeSatang),
                reservation.ReservedAt,
                clock.UtcNow);
            ManagedShippingOperationQueue
                .QueueReturnConfirmationIfRequired(
                    transaction,
                    clock.UtcNow);
        }
        else
        {
            transaction.CompleteBuyerCheckoutShipmentBooking(
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
                clock.UtcNow);
        }
        operation.Succeed(
            workerId,
            reservation.PurchaseReference,
            reservation.ProviderTrackingCode,
            clock.UtcNow);
    }

    private async Task<bool> RevalidateOutboundParcelProtectionAsync(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperation operation,
        string workerId,
        CancellationToken cancellationToken)
    {
        var change = transaction.ParcelProtectionChangeRequests
            .SingleOrDefault(request =>
                request.Status == ParcelProtectionChangeStatus.AwaitingRebooking);
        if (change is not null &&
            transaction.CurrentOutboundShipment?.Id == shipment.Id)
        {
            if (change.DesiredExpiresAt <= clock.UtcNow)
            {
                SupersedeChangedProtectionChange(
                    transaction,
                    change,
                    shipment,
                    operation,
                    workerId,
                    "parcel-protection-option-expired");
                return false;
            }
            if (change.DesiredElection == ParcelProtectionElectionStatus.Accepted)
            {
                ProviderParcelProtectionOption validated;
                try
                {
                    validated = await parcelProtectionQuotes.ValidateOptionAsync(
                        ParcelProtectionCheckout.BuildProtectionRequest(transaction),
                        change.DesiredOptionReference ?? throw new DomainException(
                            "parcel-protection-option-reference-missing"),
                        cancellationToken);
                }
                catch (ParcelProtectionOptionChangedException)
                {
                    SupersedeChangedProtectionChange(
                        transaction,
                        change,
                        shipment,
                        operation,
                        workerId,
                        "parcel-protection-quote-changed");
                    return false;
                }
                if (!MatchesChangeSelection(change, shipment, validated))
                {
                    SupersedeChangedProtectionChange(
                        transaction,
                        change,
                        shipment,
                        operation,
                        workerId,
                        "parcel-protection-quote-changed");
                    return false;
                }
            }
            else if (change.DesiredElection is
                     ParcelProtectionElectionStatus.Declined or
                     ParcelProtectionElectionStatus.Unavailable)
            {
                var availability =
                    await parcelProtectionQuotes.GetAvailabilityAsync(
                        ParcelProtectionCheckout.BuildProtectionRequest(
                            transaction),
                        cancellationToken);
                if (!MatchesUnprotectedChangeSelection(
                        transaction,
                        change,
                        shipment,
                        availability))
                {
                    SupersedeChangedProtectionChange(
                        transaction,
                        change,
                        shipment,
                        operation,
                        workerId,
                        "parcel-protection-quote-changed");
                    return false;
                }
            }
            else
            {
                SupersedeChangedProtectionChange(
                    transaction,
                    change,
                    shipment,
                    operation,
                    workerId,
                    "parcel-protection-selection-mismatch");
                return false;
            }
            return true;
        }
        if (transaction.ParcelProtectionElection ==
            ParcelProtectionElectionStatus.Accepted)
        {
            ProviderParcelProtectionOption validated;
            try
            {
                validated = await parcelProtectionQuotes.ValidateOptionAsync(
                    ParcelProtectionCheckout.BuildProtectionRequest(transaction),
                    transaction.ParcelProtectionOptionReference ??
                    throw new DomainException(
                        "parcel-protection-option-reference-missing"),
                    cancellationToken);
            }
            catch (ParcelProtectionOptionChangedException)
            {
                SupersedeChangedProtectionOption(
                    transaction,
                    shipment,
                    operation,
                    workerId);
                return false;
            }

            if (!MatchesStoredSelection(transaction, shipment, validated))
            {
                SupersedeChangedProtectionOption(
                    transaction,
                    shipment,
                    operation,
                    workerId);
                return false;
            }
            return true;
        }

        if (transaction.ParcelProtectionElection is
                ParcelProtectionElectionStatus.Declined or
                ParcelProtectionElectionStatus.Unavailable or
                ParcelProtectionElectionStatus.NotApplicable &&
            shipment.ParcelProtectionElection ==
                transaction.ParcelProtectionElection &&
            transaction.ParcelProtectionProviderCostSatang == 0 &&
            transaction.ParcelInsuranceFeeSatang == 0 &&
            shipment.ParcelProtectionProviderCostSatang == 0 &&
            shipment.InsuranceFeeSatang == 0 &&
            shipment.DeclaredValueSatang == 0 &&
            string.IsNullOrWhiteSpace(shipment.InsuranceCode))
            return true;

        throw new DomainException("parcel-protection-selection-mismatch");
    }

    private static bool MatchesChangeSelection(
        ParcelProtectionChangeRequest change,
        ManagedShipment shipment,
        ProviderParcelProtectionOption validated) =>
        string.Equals(validated.OptionReference, change.DesiredOptionReference,
            StringComparison.Ordinal) &&
        string.Equals(validated.TermsVersion, change.DesiredTermsVersion,
            StringComparison.Ordinal) &&
        validated.ProviderCostSatang == change.DesiredProviderCostSatang &&
        validated.IncludedCoverageLimitSatang ==
            change.DesiredIncludedCoverageSatang &&
        validated.SelectedCoverageLimitSatang ==
            change.DesiredSelectedCoverageSatang &&
        validated.QuotedAt == change.DesiredQuotedAt &&
        validated.ExpiresAt == change.DesiredExpiresAt &&
        string.Equals(validated.InsuranceCode, change.DesiredInsuranceCode,
            StringComparison.Ordinal) &&
        string.Equals(validated.Provider, shipment.Provider,
            StringComparison.Ordinal) &&
        shipment.ParcelProtectionElection == change.DesiredElection &&
        shipment.ParcelProtectionProviderCostSatang ==
            change.DesiredProviderCostSatang &&
        shipment.InsuranceFeeSatang == change.DesiredProviderCostSatang &&
        shipment.DeclaredValueSatang == change.DesiredSelectedCoverageSatang &&
        string.Equals(shipment.InsuranceCode, change.DesiredInsuranceCode,
            StringComparison.Ordinal);

    private static bool MatchesUnprotectedChangeSelection(
        SaleTransaction transaction,
        ParcelProtectionChangeRequest change,
        ManagedShipment shipment,
        ParcelProtectionAvailability availability)
    {
        var expectedElection =
            transaction.PriceSatang >
                availability.IncludedCoverageLimitSatang &&
            (availability.AddOn is null ||
             !availability.ProviderCapabilityCertified)
                ? ParcelProtectionElectionStatus.Unavailable
                : ParcelProtectionElectionStatus.Declined;
        return change.DesiredElection == expectedElection &&
            change.DesiredCustomerPriceSatang == 0 &&
            change.DesiredProviderCostSatang == 0 &&
            change.DesiredServiceFeeSatang == 0 &&
            change.DesiredIncludedCoverageSatang ==
                availability.IncludedCoverageLimitSatang &&
            change.DesiredSelectedCoverageSatang ==
                availability.IncludedCoverageLimitSatang &&
            string.Equals(
                change.DesiredTermsVersion,
                ParcelProtectionCheckout.IncludedTermsVersion,
                StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(change.DesiredOptionReference) &&
            string.IsNullOrWhiteSpace(change.DesiredInsuranceCode) &&
            shipment.ParcelProtectionElection == change.DesiredElection &&
            shipment.ParcelProtectionProviderCostSatang == 0 &&
            shipment.ParcelProtectionIncludedCoverageSatang ==
                change.DesiredIncludedCoverageSatang &&
            shipment.ParcelProtectionSelectedCoverageSatang ==
                change.DesiredSelectedCoverageSatang &&
            string.Equals(
                shipment.ParcelProtectionTermsVersion,
                change.DesiredTermsVersion,
                StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(
                shipment.ParcelProtectionOptionReference) &&
            shipment.InsuranceFeeSatang == 0 &&
            shipment.DeclaredValueSatang == 0 &&
            string.IsNullOrWhiteSpace(shipment.InsuranceCode);
    }

    private void SupersedeChangedProtectionOption(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperation operation,
        string workerId)
    {
        transaction.InvalidateParcelProtectionElection(
            "parcel-protection-quote-changed",
            clock.UtcNow);
        operation.Supersede(
            workerId,
            "parcel-protection-quote-changed",
            clock.UtcNow);
        RecordParcelProtectionBookingOutcome(
            transaction,
            shipment,
            operation,
            "Superseded",
            "parcel-protection-quote-changed",
            clock.UtcNow);
    }

    private void SupersedeChangedProtectionChange(
        SaleTransaction transaction,
        ParcelProtectionChangeRequest change,
        ManagedShipment shipment,
        ShippingOperation operation,
        string workerId,
        string reasonCode)
    {
        transaction.RequireParcelProtectionChangeReconfirmation(
            change.Id,
            shipment.Id,
            reasonCode,
            clock.UtcNow);
        operation.Supersede(
            workerId,
            reasonCode,
            clock.UtcNow);
        RecordParcelProtectionBookingOutcome(
            transaction,
            shipment,
            operation,
            "Superseded",
            reasonCode,
            clock.UtcNow);
    }

    private bool MatchesStoredSelection(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ProviderParcelProtectionOption validated) =>
        string.Equals(validated.Provider, shipment.Provider,
            StringComparison.Ordinal) &&
        string.Equals(validated.Provider, transaction.ShippingQuoteProvider,
            StringComparison.Ordinal) &&
        string.Equals(validated.OptionReference,
            transaction.ParcelProtectionOptionReference,
            StringComparison.Ordinal) &&
        string.Equals(validated.OptionReference,
            shipment.ParcelProtectionOptionReference,
            StringComparison.Ordinal) &&
        string.Equals(validated.TermsVersion,
            transaction.ParcelProtectionTermsVersion,
            StringComparison.Ordinal) &&
        string.Equals(validated.TermsVersion,
            shipment.ParcelProtectionTermsVersion,
            StringComparison.Ordinal) &&
        validated.ProviderCostSatang ==
            transaction.ParcelProtectionProviderCostSatang &&
        validated.ProviderCostSatang ==
            shipment.ParcelProtectionProviderCostSatang &&
        transaction.ParcelProtectionServiceFeeSatang ==
            SaleTransaction.ParcelProtectionServiceFeeAmountSatang &&
        transaction.ParcelInsuranceFeeSatang == checked(
            transaction.ParcelProtectionProviderCostSatang +
            transaction.ParcelProtectionServiceFeeSatang) &&
        validated.IncludedCoverageLimitSatang ==
            transaction.ParcelProtectionIncludedCoverageSatang &&
        validated.IncludedCoverageLimitSatang ==
            shipment.ParcelProtectionIncludedCoverageSatang &&
        validated.SelectedCoverageLimitSatang ==
            transaction.ParcelProtectionSelectedCoverageSatang &&
        validated.SelectedCoverageLimitSatang ==
            shipment.ParcelProtectionSelectedCoverageSatang &&
        string.Equals(validated.InsuranceCode, shipment.InsuranceCode,
            StringComparison.Ordinal) &&
        validated.QuotedAt == transaction.ParcelProtectionQuotedAt &&
        validated.ExpiresAt == transaction.ParcelProtectionExpiresAt &&
        validated.ExpiresAt > clock.UtcNow;

    private static void RecordParcelProtectionBookingOutcome(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperation operation,
        string outcome,
        string reasonCode,
        DateTimeOffset now)
    {
        if (operation.OperationType == ShippingOperationType.BookOutbound)
            transaction.RecordParcelProtectionBookingOutcome(
                shipment.Id,
                operation.Id,
                outcome,
                reasonCode,
                now);
    }

    private async Task ConfirmAsync(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperation operation,
        string workerId,
        CancellationToken cancellationToken)
    {
        var confirmation = await provider.ConfirmServiceAsync(
            shipment.PurchaseReference ??
            throw new DomainException(
                "shipping-purchase-reference-missing"),
            shipment.ProviderTrackingCode ??
            throw new DomainException(
                "shipping-tracking-reference-missing"),
            shipment.CarrierCode,
            shipment.ServiceCode,
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
        await provider.CancelServiceAsync(
            shipment.CourierTrackingCode ??
            shipment.ProviderTrackingCode ??
            throw new DomainException(
                "shipping-cancellation-reference-missing"),
            shipment.ServiceCode,
            shipment.Direction ==
                ShipmentDirection.Return,
            cancellationToken);
        shipment.RecordCancellation(clock.UtcNow);
        if (shipment.Direction == ShipmentDirection.Outbound)
        {
            var change = transaction.ParcelProtectionChangeRequests
                .SingleOrDefault(request =>
                    request.PreviousManagedShipmentId == shipment.Id &&
                    request.Status ==
                        ParcelProtectionChangeStatus.AwaitingCancellation);
            if (change is not null)
            {
                transaction.CompleteParcelProtectionCancellation(
                    shipment.Id, clock.UtcNow);
                if (change.DesiredExpiresAt <= clock.UtcNow)
                    transaction.RequireParcelProtectionChangeReconfirmation(
                        change.Id,
                        null,
                        "parcel-protection-option-expired",
                        clock.UtcNow);
                else
                {
                    var replacement = transaction.CreateReplacementOutboundShipment(
                        change.Id, clock.UtcNow);
                    transaction.QueueReplacementOutboundShipmentIntent(
                        replacement, change.Id, clock.UtcNow);
                }
            }
            else
                transaction.RecordShippingCancellation(
                    shipment.Provider, clock.UtcNow);
        }
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

    private static string CurrentFingerprint(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShippingOperationType operationType) =>
        operationType switch
        {
            ShippingOperationType.BookOutbound or
                ShippingOperationType.BookReturn =>
                ManagedShippingOperationQueue.BookingFingerprint(
                    shipment),
            ShippingOperationType.ConfirmOutbound or
                ShippingOperationType.ConfirmReturn =>
                ManagedShippingOperationQueue
                    .ConfirmationFingerprint(
                        transaction,
                        shipment),
            ShippingOperationType.CancelOutbound or
                ShippingOperationType.CancelReturn =>
                ManagedShippingOperationQueue
                    .CancellationFingerprint(
                        transaction,
                        shipment),
            _ => throw new DomainException(
                "shipping-operation-unsupported")
        };

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
