using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Features.Checkout.GetParcelProtection;
using Toklong.Application.Features.Shipping;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Checkout.BookShipmentForPayment;

public enum DirectBookingState
{
    Ready,
    InProgress,
    Failed,
    TimedOut,
    RetryLimitReached,
    ReconfirmationRequired
}

public sealed record DirectBookingResult(
    DirectBookingState State,
    Guid AttemptId,
    string? SafeCode);

public sealed record BookShipmentForPaymentCommand(
    Guid TransactionId,
    Guid BuyerId,
    string IdempotencyKey)
    : IRequest<DirectBookingResult>;

public interface IDirectCheckoutBooking
{
    Task<DirectBookingResult> BookAsync(
        SaleTransaction transaction,
        Guid buyerId,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed class BookShipmentForPaymentHandler(
    ITransactionRepository transactions,
    IBookingAttemptRepository attempts,
    IShipmentProvider provider,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
          BookShipmentForPaymentCommand,
          DirectBookingResult>,
      IDirectCheckoutBooking
{
    private static readonly TimeSpan ProviderBudget =
        TimeSpan.FromMilliseconds(2_200);

    public async Task<DirectBookingResult> Handle(
        BookShipmentForPaymentCommand request,
        CancellationToken cancellationToken)
    {
        var transaction = await transactions.GetByIdAsync(
            request.TransactionId,
            cancellationToken) ??
            throw new DomainException("ไม่พบรายการ");
        return await BookAsync(
            transaction,
            request.BuyerId,
            request.IdempotencyKey,
            cancellationToken);
    }

    public async Task<DirectBookingResult> BookAsync(
        SaleTransaction transaction,
        Guid buyerId,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.BuyerId != buyerId ||
            transaction.State !=
                TransactionState.SellerAcceptedAwaitingPayment ||
            transaction.FulfillmentType !=
                FulfillmentType.PhysicalShipment)
            throw new DomainException(
                "รายการนี้ยังเตรียมการจัดส่งเพื่อชำระเงินไม่ได้");
        if (!transaction.BuyerPaymentDeadlineAt.HasValue ||
            transaction.BuyerPaymentDeadlineAt <= clock.UtcNow)
            throw new DomainException(
                "หมดเวลาชำระแล้ว กรุณาส่งข้อเสนอใหม่ให้ผู้ขายยืนยัน");
        if (transaction.ParcelProtectionElection is
            ParcelProtectionElectionStatus.Pending or
            ParcelProtectionElectionStatus
                .ReconfirmationRequired)
            throw new DomainException(
                "กรุณายืนยันความคุ้มครองพัสดุก่อนชำระเงิน");

        var cleanKey =
            ParcelProtectionCheckout.RequireSafeIdempotencyKey(
                idempotencyKey);
        var shipment = transaction.CurrentOutboundShipment ??
            throw new DomainException(
                "ไม่พบข้อมูลจัดส่งที่ยืนยัน");
        var requestFingerprint =
            ManagedShippingOperationQueue.BookingFingerprint(
                shipment);
        var acquired = await attempts.AcquireAsync(
            new AcquireBookingAttempt(
                transaction.Id,
                shipment.Id,
                buyerId,
                cleanKey,
                requestFingerprint,
                clock.UtcNow),
            cancellationToken);

        if (acquired.State !=
            BookingAttemptAcquireState.Acquired)
            return await ResolveExistingAsync(
                transaction,
                shipment,
                acquired,
                cancellationToken);

        var attempt = acquired.Attempt;
        try
        {
            var request = BuildReservationRequest(
                transaction,
                shipment,
                attempt.ProviderReference);
            using var budget =
                CancellationTokenSource
                    .CreateLinkedTokenSource(
                        cancellationToken);
            budget.CancelAfter(ProviderBudget);
            var reservation = await provider.ReserveAsync(
                request,
                budget.Token);
            if (!Matches(shipment, reservation))
            {
                attempt.Fail(
                    "shipping-option-changed",
                    clock.UtcNow);
                await unitOfWork.SaveChangesAsync(
                    CancellationToken.None);
                return new(
                    DirectBookingState
                        .ReconfirmationRequired,
                    attempt.Id,
                    "shipping-option-changed");
            }

            var success = Success(
                transaction.Currency,
                reservation);
            attempt.Succeed(
                success,
                clock.UtcNow);
            ApplySuccess(
                transaction,
                shipment,
                reservation,
                clock.UtcNow);
            await unitOfWork.SaveChangesAsync(
                cancellationToken);
            return new(
                DirectBookingState.Ready,
                attempt.Id,
                null);
        }
        catch (OperationCanceledException)
            when (!cancellationToken
                .IsCancellationRequested)
        {
            attempt.TimeOut(
                "shippop-timeout",
                clock.UtcNow);
            await unitOfWork.SaveChangesAsync(
                CancellationToken.None);
            return new(
                DirectBookingState.TimedOut,
                attempt.Id,
                "shippop-timeout");
        }
        catch (ShipmentMutationException exception)
        {
            if (exception.Outcome ==
                ShipmentMutationOutcome.OutcomeUnknown)
            {
                attempt.TimeOut(
                    exception.SanitizedCode,
                    clock.UtcNow);
                await unitOfWork.SaveChangesAsync(
                    CancellationToken.None);
                return new(
                    DirectBookingState.TimedOut,
                    attempt.Id,
                    exception.SanitizedCode);
            }

            attempt.Fail(
                exception.SanitizedCode,
                clock.UtcNow);
            await unitOfWork.SaveChangesAsync(
                CancellationToken.None);
            return new(
                DirectBookingState.Failed,
                attempt.Id,
                exception.SanitizedCode);
        }
    }

    private async Task<DirectBookingResult>
        ResolveExistingAsync(
            SaleTransaction transaction,
            ManagedShipment shipment,
            AcquireBookingAttemptResult acquired,
            CancellationToken cancellationToken)
    {
        var attempt = acquired.Attempt;
        if (acquired.State ==
                BookingAttemptAcquireState.Succeeded &&
            !transaction.ParcelProtectionBookingReady)
        {
            var reservation =
                ReservationFrom(attempt, shipment);
            ApplySuccess(
                transaction,
                shipment,
                reservation,
                clock.UtcNow);
            await unitOfWork.SaveChangesAsync(
                cancellationToken);
        }

        return acquired.State switch
        {
            BookingAttemptAcquireState.Succeeded =>
                new(
                    DirectBookingState.Ready,
                    attempt.Id,
                    null),
            BookingAttemptAcquireState.InProgress =>
                new(
                    DirectBookingState.InProgress,
                    attempt.Id,
                    "preparing-shipping"),
            BookingAttemptAcquireState.TimedOut =>
                new(
                    DirectBookingState.TimedOut,
                    attempt.Id,
                    attempt.SafeFailureCode),
            BookingAttemptAcquireState
                .RetryLimitReached =>
                new(
                    DirectBookingState
                        .RetryLimitReached,
                    attempt.Id,
                    "booking-retry-limit"),
            BookingAttemptAcquireState
                .FingerprintConflict =>
                new(
                    DirectBookingState.Failed,
                    attempt.Id,
                    "idempotency-conflict"),
            _ => new(
                DirectBookingState.Failed,
                attempt.Id,
                attempt.SafeFailureCode ??
                "shipping-preparation-failed")
        };
    }

    private static ShipmentReservationRequest
        BuildReservationRequest(
            SaleTransaction transaction,
            ManagedShipment shipment,
            string operationReference)
    {
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
        return new(
            transaction.Id,
            ParcelProtectionCheckout
                .BuildShipmentRequest(transaction),
            quote,
            shipment.Id,
            false,
            operationReference);
    }

    private static bool Matches(
        ManagedShipment shipment,
        ShipmentReservation result) =>
        string.Equals(
            shipment.Provider,
            result.Provider,
            StringComparison.Ordinal) &&
        string.Equals(
            shipment.CarrierCode,
            result.CarrierCode,
            StringComparison.Ordinal) &&
        string.Equals(
            shipment.ServiceCode,
            result.ServiceCode,
            StringComparison.Ordinal) &&
        shipment.BaseShippingFeeSatang ==
            result.FeeSatang &&
        shipment.InsuranceFeeSatang ==
            result.InsuranceFeeSatang &&
        shipment.DeclaredValueSatang ==
            result.DeclaredValueSatang &&
        string.Equals(
            shipment.InsuranceCode,
            result.InsuranceCode,
            StringComparison.Ordinal);

    private static BookingAttemptSuccess Success(
        string currency,
        ShipmentReservation reservation) => new(
        reservation.PurchaseReference,
        reservation.ProviderTrackingCode,
        reservation.CourierTrackingCode,
        reservation.FeeSatang,
        reservation.InsuranceFeeSatang,
        reservation.DeclaredValueSatang,
        currency,
        Fingerprint(
            JsonSerializer.Serialize(new
            {
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
                Currency =
                    currency.ToUpperInvariant()
            })));

    private static ShipmentReservation ReservationFrom(
        BookingAttempt attempt,
        ManagedShipment shipment) => new(
        shipment.Provider,
        attempt.ProviderPurchaseId ??
            throw new DomainException(
                "ผลเตรียมจัดส่งไม่ครบ"),
        attempt.ProviderTrackingCode ??
            throw new DomainException(
                "ผลเตรียมจัดส่งไม่ครบ"),
        attempt.CourierTrackingCode,
        shipment.CarrierCode,
        shipment.ServiceCode,
        attempt.QuotedShippingFeeSatang ??
            throw new DomainException(
                "ผลเตรียมจัดส่งไม่ครบ"),
        attempt.QuotedProtectionFeeSatang ??
            throw new DomainException(
                "ผลเตรียมจัดส่งไม่ครบ"),
        attempt.QuotedCoverageLimitSatang ??
            throw new DomainException(
                "ผลเตรียมจัดส่งไม่ครบ"),
        shipment.InsuranceCode,
        attempt.CompletedAt ??
            throw new DomainException(
                "ผลเตรียมจัดส่งไม่ครบ"));

    private static void ApplySuccess(
        SaleTransaction transaction,
        ManagedShipment shipment,
        ShipmentReservation reservation,
        DateTimeOffset completedAt) =>
        transaction
            .CompleteBuyerCheckoutShipmentBooking(
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
                completedAt);

    private static string Fingerprint(
        string value) =>
        Convert.ToHexString(
                SHA256.HashData(
                    Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
}
