using MediatR;
using Toklong.Application.Abstractions;
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
    IUnitOfWork unitOfWork,
    IClock clock)
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
        var pending =
            await pendingRegistrations.GetByTicketHashAsync(
                ticketHash,
                cancellationToken)
            ?? throw new ArgumentException(
                "การยืนยันเบอร์หมดอายุ กรุณายืนยันเบอร์ใหม่");

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

        var buyer = BuyerAccount.Create(
            pending.PhoneNumber,
            AccountName.Create(request.FirstName, request.LastName),
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

        return ProfileFor(buyer);
    }

    private static MobileSessionProfile ProfileFor(BuyerAccount buyer) =>
        new(
            buyer.Id,
            null,
            buyer.PhoneNumber,
            buyer.FullName);
}
