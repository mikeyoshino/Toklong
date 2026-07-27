using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;

namespace Toklong.Application.Features.Authentication;

public enum MobileAuthenticationMode
{
    SignIn,
    SignUp
}

public sealed record VerifyMobileCodeCommand(
    string ChallengeId,
    string Code,
    MobileAuthenticationMode Mode,
    string? InstallationId)
    : IRequest<MobileCodeVerificationResult>;

public sealed record MobileSessionProfile(
    Guid? BuyerId,
    Guid? SellerId,
    string PhoneNumber,
    string DisplayName);

public sealed record PendingRegistrationResult(
    string RegistrationTicket,
    DateTimeOffset ExpiresAt,
    string MaskedPhoneNumber);

public sealed record MobileCodeVerificationResult(
    MobileSessionProfile? Session,
    PendingRegistrationResult? Registration)
{
    public static MobileCodeVerificationResult ForSession(
        MobileSessionProfile profile) =>
        new(profile, null);

    public static MobileCodeVerificationResult ForRegistration(
        string registrationTicket,
        DateTimeOffset expiresAt,
        string maskedPhoneNumber) =>
        new(
            null,
            new PendingRegistrationResult(
                registrationTicket,
                expiresAt,
                maskedPhoneNumber));
}

public sealed class VerifyMobileCodeHandler(
    IOtpVerificationProvider provider,
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IPendingMobileRegistrationRepository pendingRegistrations,
    IRegistrationTicketService tickets,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        VerifyMobileCodeCommand,
        MobileCodeVerificationResult>
{
    public async Task<MobileCodeVerificationResult> Handle(
        VerifyMobileCodeCommand request,
        CancellationToken cancellationToken)
    {
        var phone = await provider.VerifyAsync(
            request.ChallengeId,
            request.Code,
            cancellationToken);
        if (phone is null)
            throw new ArgumentException(
                "รหัสไม่ถูกต้อง ใช้ไปแล้ว หรือหมดอายุ กรุณาขอรหัสใหม่");

        var buyer = await buyers.GetByPhoneAsync(
            phone,
            cancellationToken);
        var seller = await sellers.GetByPhoneAsync(
            phone,
            cancellationToken);

        if (request.Mode == MobileAuthenticationMode.SignIn)
        {
            if (buyer is null && seller is null)
                throw new ArgumentException(
                    "ยังไม่มีบัญชีสำหรับเบอร์นี้ กรุณาสมัครสมาชิกก่อน");
            RefreshPhoneProof(buyer, seller, phone);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return MobileCodeVerificationResult.ForSession(
                ProfileFor(buyer, seller, phone));
        }

        if (buyer is not null || seller is not null)
        {
            RefreshPhoneProof(buyer, seller, phone);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return MobileCodeVerificationResult.ForSession(
                ProfileFor(buyer, seller, phone));
        }

        var pair = tickets.Issue();
        var expiresAt = clock.UtcNow.AddMinutes(15);
        await pendingRegistrations.AddAsync(
            PendingMobileRegistration.Create(
                pair.TicketHash,
                phone,
                RequiredInstallationId(request.InstallationId),
                clock.UtcNow,
                expiresAt),
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MobileCodeVerificationResult.ForRegistration(
            pair.RawTicket,
            expiresAt,
            MaskLocalPhone(phone));
    }

    private void RefreshPhoneProof(
        BuyerAccount? buyer,
        SellerAccount? seller,
        string phone)
    {
        buyer?.UpdatePhoneVerification(phone, clock.UtcNow);
        seller?.MarkPhoneVerified(clock.UtcNow);
    }

    private static MobileSessionProfile ProfileFor(
        BuyerAccount? buyer,
        SellerAccount? seller,
        string phone) =>
        new(
            buyer?.Id,
            seller?.Id,
            phone,
            buyer?.FullName ??
            seller?.DisplayName ??
            "ผู้ใช้ TOKLONG");

    private static string RequiredInstallationId(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException(
                "ไม่พบข้อมูลอุปกรณ์ กรุณาเริ่มสมัครสมาชิกใหม่")
            : value;

    private static string MaskLocalPhone(string phone)
    {
        var local = $"0{phone[3..]}";
        return $"{local[..3]}-***-{local[^4..]}";
    }
}
