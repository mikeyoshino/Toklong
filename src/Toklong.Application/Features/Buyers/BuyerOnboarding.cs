using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Buyers;

namespace Toklong.Application.Features.Buyers;

public sealed record RequestBuyerOtpCommand(string PhoneNumber)
    : IRequest<OtpChallenge>;

public sealed class RequestBuyerOtpHandler(IOtpVerificationProvider provider)
    : IRequestHandler<RequestBuyerOtpCommand, OtpChallenge>
{
    public Task<OtpChallenge> Handle(
        RequestBuyerOtpCommand request,
        CancellationToken cancellationToken) =>
        provider.RequestAsync(
            ThaiMobilePhone.Normalize(request.PhoneNumber),
            cancellationToken);
}

public sealed record VerifyBuyerOtpCommand(
    string ChallengeId,
    string Code) : IRequest<BuyerProfile>;

public sealed class VerifyBuyerOtpHandler(
    IOtpVerificationProvider provider,
    IBuyerRepository buyers,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<VerifyBuyerOtpCommand, BuyerProfile>
{
    public async Task<BuyerProfile> Handle(
        VerifyBuyerOtpCommand request,
        CancellationToken cancellationToken)
    {
        var phone = await provider.VerifyAsync(
            request.ChallengeId, request.Code, cancellationToken);
        if (phone is null)
            throw new ArgumentException(
                "รหัสไม่ถูกต้อง ใช้ไปแล้ว หรือหมดอายุ กรุณาขอรหัสใหม่");

        var buyer = await buyers.GetByPhoneAsync(phone, cancellationToken);
        if (buyer is null)
            throw new ArgumentException(
                "ยังไม่มีบัญชีสำหรับเบอร์นี้ กรุณาสมัครสมาชิกก่อน");

        buyer.UpdatePhoneVerification(phone, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BuyerProfile.From(buyer);
    }
}

public sealed record RegisterBuyerCommand(
    string ChallengeId,
    string Code,
    string FullName,
    string Email) : IRequest<BuyerProfile>;

public sealed class RegisterBuyerHandler(
    IOtpVerificationProvider provider,
    IBuyerRepository buyers,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<RegisterBuyerCommand, BuyerProfile>
{
    public async Task<BuyerProfile> Handle(
        RegisterBuyerCommand request,
        CancellationToken cancellationToken)
    {
        var phone = await provider.VerifyAsync(
            request.ChallengeId, request.Code, cancellationToken);
        if (phone is null)
            throw new ArgumentException(
                "รหัสไม่ถูกต้อง ใช้ไปแล้ว หรือหมดอายุ กรุณาขอรหัสใหม่");

        if (await buyers.GetByPhoneAsync(phone, cancellationToken) is not null)
            throw new ArgumentException(
                "เบอร์นี้มีบัญชีแล้ว กรุณาเข้าสู่ระบบ");

        var buyer = BuyerAccount.Create(
            phone,
            request.FullName,
            request.Email,
            clock.UtcNow);
        await buyers.AddAsync(buyer, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BuyerProfile.From(buyer);
    }
}

public sealed record GetBuyerProfileQuery(Guid BuyerId)
    : IRequest<BuyerProfile>;

public sealed class GetBuyerProfileHandler(IBuyerRepository buyers)
    : IRequestHandler<GetBuyerProfileQuery, BuyerProfile>
{
    public async Task<BuyerProfile> Handle(
        GetBuyerProfileQuery request,
        CancellationToken cancellationToken)
    {
        var buyer = await buyers.GetByIdAsync(request.BuyerId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ซื้อ");
        return BuyerProfile.From(buyer);
    }
}

public sealed record BuyerProfile(
    Guid Id,
    string PhoneNumber,
    string FullName,
    string FirstName,
    string LastName,
    string? Email,
    DateTimeOffset PhoneVerifiedAt,
    BuyerSavedAddressView? SavedDeliveryAddress)
{
    public static BuyerProfile From(BuyerAccount buyer) =>
        new(
            buyer.Id,
            buyer.PhoneNumber,
            buyer.FullName,
            buyer.FirstName,
            buyer.LastName,
            buyer.Email,
            buyer.PhoneVerifiedAt,
            BuyerSavedAddressView.From(
                buyer.GetSavedDeliveryAddress(),
                buyer.SavedAddressUpdatedAt));
}

public sealed record BuyerSavedAddressView(
    string AddressLine,
    int ProvinceId,
    string ProvinceName,
    int DistrictId,
    string DistrictName,
    int SubdistrictId,
    string SubdistrictName,
    string PostalCode,
    string DisplayText,
    DateTimeOffset? UpdatedAt)
{
    public static BuyerSavedAddressView? From(
        BuyerDeliveryAddress? address,
        DateTimeOffset? updatedAt) =>
        address is null
            ? null
            : new(
                address.AddressLine,
                address.ProvinceId,
                address.ProvinceName,
                address.DistrictId,
                address.DistrictName,
                address.SubdistrictId,
                address.SubdistrictName,
                address.PostalCode,
                address.ToDisplayText(),
                updatedAt);
}
