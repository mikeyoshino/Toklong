using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Application.Features.Checkout.BookShipmentForPayment;
using Toklong.Application.Pricing;
using Toklong.Application.Transactions;
using Toklong.Domain.Common;
using Toklong.Domain.Transactions;

namespace Toklong.Application.Features.Checkout.PreparePaymentSheet;

public sealed record PreparePaymentSheetCommand(
    Guid TransactionId,
    Guid BuyerId,
    bool AcceptedTerms,
    string IdempotencyKey = "legacy-payment-sheet")
    : IRequest<PreparedPaymentSheet>;

public sealed class CheckoutBookingException(
    DirectBookingState state,
    string? safeCode)
    : Exception(
        safeCode ??
        "shipping-preparation-failed")
{
    public DirectBookingState State { get; } = state;
    public string SafeCode { get; } =
        safeCode ??
        "shipping-preparation-failed";
}

public sealed record PreparedPaymentSheet(
    TransactionView Transaction,
    string ClientSecret,
    string PublishableKey,
    string ReceiptEmail);

public sealed class PreparePaymentSheetHandler(
    ITransactionRepository repository,
    IBuyerRepository buyers,
    IPaymentIntentProvider paymentIntents,
    IPaymentFeePolicy feePolicy,
    IUnitOfWork unitOfWork,
    IClock clock,
    TransactionTransitionService transitions,
    IDirectCheckoutBooking? directBooking = null)
    : IRequestHandler<PreparePaymentSheetCommand, PreparedPaymentSheet>
{
    public async Task<PreparedPaymentSheet> Handle(
        PreparePaymentSheetCommand request,
        CancellationToken cancellationToken)
    {
        if (!request.AcceptedTerms)
            throw new ArgumentException("กรุณายอมรับข้อตกลงของรายการก่อนชำระ");

        var transaction = await repository.GetByIdAsync(
            request.TransactionId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบรายการ");
        if (transaction.BuyerId != request.BuyerId)
            throw new DomainException("บัญชีผู้ซื้อนี้ไม่มีสิทธิ์ชำระข้อเสนอ");
        var now = clock.UtcNow;
        if (transaction.ExpireIfDue(now, transitions))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
            throw new DomainException(
                "หมดเวลาชำระแล้ว กรุณาส่งข้อเสนอใหม่ให้ผู้ขายยืนยัน");
        }
        if (transaction.State is not (
                TransactionState.SellerAcceptedAwaitingPayment or
                TransactionState.PaymentPending))
            throw new DomainException("รายการนี้ยังไม่พร้อมให้ชำระเงิน");
        if (transaction.State == TransactionState.PaymentPending &&
            !string.Equals(
                transaction.PaymentProvider,
                "stripe",
                StringComparison.Ordinal))
            throw new DomainException(
                "รายการนี้เริ่มชำระด้วยช่องทางอื่นแล้ว");
        if (transaction.FulfillmentType ==
                FulfillmentType.PhysicalShipment &&
            !transaction.ParcelProtectionBookingReady)
        {
            if (directBooking is null)
                throw new DomainException(
                    "กำลังเตรียมรายการจัดส่ง กรุณาลองอีกครั้ง");
            var booking = await directBooking.BookAsync(
                transaction,
                request.BuyerId,
                request.IdempotencyKey,
                cancellationToken);
            if (booking.State !=
                DirectBookingState.Ready)
                throw new CheckoutBookingException(
                    booking.State,
                    booking.SafeCode);
            if (!transaction
                .ParcelProtectionBookingReady)
                throw new CheckoutBookingException(
                    DirectBookingState.Failed,
                    "shipping-booking-not-committed");
        }

        var buyer = await buyers.GetByIdAsync(
            request.BuyerId,
            cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ซื้อ");
        if (string.IsNullOrWhiteSpace(buyer.Email))
            throw new DomainException(
                "บัญชีนี้ยังไม่มีอีเมล กรุณาเพิ่มอีเมลในหน้าบัญชีก่อนชำระ");
        var fees = feePolicy.Calculate(transaction.PriceSatang);
        if (transaction.BuyerProtectionFeeSatang !=
                fees.BuyerProtectionFeeSatang ||
            transaction.PlatformFeeSatang != fees.PlatformFeeSatang ||
            transaction.SellerExpectedNetSatang !=
            fees.SellerExpectedNetSatang ||
            !string.Equals(
                transaction.FeePolicyVersion,
                fees.PolicyVersion,
                StringComparison.Ordinal))
            throw new DomainException(
                "นโยบายค่าบริการเปลี่ยนหลังผู้ขายยืนยัน กรุณายกเลิกรายการและสร้างข้อเสนอใหม่");
        var prepared = await paymentIntents.PrepareAsync(
            transaction.Id,
            transaction.BuyerTotalSatang,
            transaction.Currency,
            transaction.FulfillmentType,
            buyer.Email,
            transaction.State == TransactionState.PaymentPending
                ? transaction.PaymentReference
                : null,
            cancellationToken);

        if (transaction.State == TransactionState.SellerAcceptedAwaitingPayment)
        {
            transaction.BeginCheckout(
                buyer.FullName,
                buyer.PhoneNumber,
                now,
                transitions,
                "stripe",
                prepared.ProviderReference,
                fees.BuyerProtectionFeeSatang,
                fees.PlatformFeeSatang,
                fees.SellerExpectedNetSatang,
                fees.PolicyVersion);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return new PreparedPaymentSheet(
            TransactionView.From(transaction),
            prepared.ClientSecret,
            prepared.PublishableKey,
            buyer.Email);
    }
}
