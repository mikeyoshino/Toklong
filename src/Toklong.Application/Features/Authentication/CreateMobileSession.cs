using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Buyers;

namespace Toklong.Application.Features.Authentication;

public enum MobileAuthenticationMode
{
    SignIn,
    SignUp
}

public sealed record CreateMobileSessionCommand(
    string ChallengeId,
    string Code,
    MobileAuthenticationMode Mode,
    string? FullName,
    string? Email) : IRequest<MobileSessionProfile>;

public sealed record MobileSessionProfile(
    Guid? BuyerId,
    Guid? SellerId,
    string PhoneNumber,
    string DisplayName);

public sealed class CreateMobileSessionHandler(
    IOtpVerificationProvider provider,
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<CreateMobileSessionCommand, MobileSessionProfile>
{
    public async Task<MobileSessionProfile> Handle(
        CreateMobileSessionCommand request,
        CancellationToken cancellationToken)
    {
        var phone = await provider.VerifyAsync(
            request.ChallengeId,
            request.Code,
            cancellationToken);
        if (phone is null)
            throw new ArgumentException(
                "รหัสไม่ถูกต้อง ใช้ไปแล้ว หรือหมดอายุ กรุณาขอรหัสใหม่");

        var buyer = await buyers.GetByPhoneAsync(phone, cancellationToken);
        var seller = await sellers.GetByPhoneAsync(phone, cancellationToken);

        if (request.Mode == MobileAuthenticationMode.SignUp)
        {
            if (buyer is not null)
                throw new ArgumentException("เบอร์นี้มีบัญชีแล้ว กรุณาเข้าสู่ระบบ");

            buyer = BuyerAccount.Create(
                phone,
                request.FullName ?? "",
                request.Email ?? "",
                clock.UtcNow);
            await buyers.AddAsync(buyer, cancellationToken);
        }
        else
        {
            if (buyer is null && seller is null)
                throw new ArgumentException(
                    "ยังไม่มีบัญชีสำหรับเบอร์นี้ กรุณาสมัครสมาชิกก่อน");

            buyer?.UpdatePhoneVerification(phone, clock.UtcNow);
            seller?.MarkPhoneVerified(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new MobileSessionProfile(
            buyer?.Id,
            seller?.Id,
            phone,
            buyer?.FullName ?? seller?.DisplayName ?? "ผู้ใช้ TOKLONG");
    }
}
