using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;

namespace Toklong.Application.Features.Authentication;

public sealed record CompleteMobileRegistrationCommand(
    string RegistrationTicket,
    string FirstName,
    string LastName,
    string Email,
    string TermsVersion,
    string InstallationId,
    string IdempotencyKey) : IRequest<MobileSessionProfile>;

public sealed class CompleteMobileRegistrationHandler(
    IRegistrationTicketService tickets,
    IPendingMobileRegistrationRepository pendingRegistrations,
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IUnitOfWork unitOfWork,
    IClock clock,
    IAccountPhoneTransactionManager phoneTransactions)
    : IRequestHandler<
        CompleteMobileRegistrationCommand,
        MobileSessionProfile>
{
    public const string CurrentTermsVersion = "terms-mvp-v1";

    public async Task<MobileSessionProfile> Handle(
        CompleteMobileRegistrationCommand request,
        CancellationToken cancellationToken)
    {
        var ticketHash = tickets.Hash(request.RegistrationTicket);
        var preflightPhone =
            await pendingRegistrations.GetPhoneByTicketHashAsync(
                ticketHash,
                cancellationToken)
            ?? throw new ArgumentException(
                "การยืนยันเบอร์หมดอายุ กรุณายืนยันเบอร์ใหม่");
        var normalizedPhone = ThaiMobilePhone.Normalize(preflightPhone);
        await using var phoneTransaction =
            await phoneTransactions.BeginAsync(
                normalizedPhone,
                cancellationToken);
        var pending =
            await pendingRegistrations.GetByTicketHashAsync(
                ticketHash,
                cancellationToken)
            ?? throw new ArgumentException(
                "การยืนยันเบอร์หมดอายุ กรุณายืนยันเบอร์ใหม่");
        if (!string.Equals(
                normalizedPhone,
                ThaiMobilePhone.Normalize(pending.PhoneNumber),
                StringComparison.Ordinal))
            throw new InvalidOperationException(
                "หมายเลขโทรศัพท์ของคำขอสมัครสมาชิกเปลี่ยนแปลงระหว่างดำเนินการ");

        var status = pending.ValidateCompletion(
            request.InstallationId,
            request.IdempotencyKey,
            clock.UtcNow);
        if (status == RegistrationCompletionStatus.ExactReplay)
        {
            var replayBuyer = await buyers.GetByIdAsync(
                pending.BuyerId!.Value,
                cancellationToken);
            if (replayBuyer is null)
                throw new InvalidOperationException(
                    "ไม่พบบัญชีจากคำขอสมัครสมาชิกเดิม");
            await phoneTransaction.CommitAsync(cancellationToken);
            return ProfileFor(replayBuyer);
        }

        if (!string.Equals(
                request.TermsVersion?.Trim(),
                CurrentTermsVersion,
                StringComparison.Ordinal))
            throw new ArgumentException(
                "ข้อกำหนดการใช้งานมีการเปลี่ยนแปลง กรุณาตรวจสอบอีกครั้ง");

        if (await buyers.GetByPhoneAsync(
                pending.PhoneNumber,
                cancellationToken) is not null)
            throw new ArgumentException(
                "เบอร์นี้มีบัญชีแล้ว กรุณาเข้าสู่ระบบ");

        var requestedName =
            AccountName.Create(request.FirstName, request.LastName);
        var seller = await sellers.GetByPhoneAsync(
            normalizedPhone,
            cancellationToken);
        var accountName = seller is not null &&
                          !string.IsNullOrWhiteSpace(seller.FirstName) &&
                          !string.IsNullOrWhiteSpace(seller.LastName)
            ? AccountName.Create(seller.FirstName, seller.LastName)
            : requestedName;
        var buyer = BuyerAccount.Create(
            normalizedPhone,
            accountName,
            request.Email,
            clock.UtcNow);
        var acceptance = MobileAccountTermsAcceptance.Create(
            buyer.Id,
            CurrentTermsVersion,
            request.InstallationId,
            request.IdempotencyKey,
            clock.UtcNow);

        pending.Complete(
            buyer.Id,
            request.IdempotencyKey,
            clock.UtcNow);
        await buyers.AddAsync(buyer, cancellationToken);
        await pendingRegistrations.AddAcceptanceAsync(
            acceptance,
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await phoneTransaction.CommitAsync(cancellationToken);

        return ProfileFor(buyer);
    }

    private static MobileSessionProfile ProfileFor(BuyerAccount buyer) =>
        new(
            buyer.Id,
            null,
            buyer.PhoneNumber,
            buyer.FullName);
}
