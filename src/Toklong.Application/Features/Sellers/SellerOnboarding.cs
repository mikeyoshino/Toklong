using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Sellers;

namespace Toklong.Application.Features.Sellers;

public sealed record RequestSellerOtpCommand(string PhoneNumber)
    : IRequest<OtpChallenge>;

public sealed class RequestSellerOtpHandler(IOtpVerificationProvider provider)
    : IRequestHandler<RequestSellerOtpCommand, OtpChallenge>
{
    public Task<OtpChallenge> Handle(
        RequestSellerOtpCommand request,
        CancellationToken cancellationToken) =>
        provider.RequestAsync(request.PhoneNumber, cancellationToken);
}

public sealed record VerifySellerOtpCommand(string ChallengeId, string Code)
    : IRequest<SellerProfile>;

public sealed class VerifySellerOtpHandler(
    IOtpVerificationProvider provider,
    ISellerRepository sellers,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<VerifySellerOtpCommand, SellerProfile>
{
    public async Task<SellerProfile> Handle(
        VerifySellerOtpCommand request,
        CancellationToken cancellationToken)
    {
        var phone = await provider.VerifyAsync(
            request.ChallengeId, request.Code, cancellationToken);
        if (phone is null)
            throw new ArgumentException("รหัส OTP ไม่ถูกต้องหรือหมดอายุแล้ว");

        var seller = await sellers.GetByPhoneAsync(phone, cancellationToken);
        if (seller is null)
        {
            seller = SellerAccount.Create(phone, clock.UtcNow);
            await sellers.AddAsync(seller, cancellationToken);
        }
        else
        {
            seller.MarkPhoneVerified(clock.UtcNow);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SellerProfile.From(seller);
    }
}

public sealed record GetSellerProfileQuery(Guid SellerId)
    : IRequest<SellerProfile>;

public sealed class GetSellerProfileHandler(ISellerRepository sellers)
    : IRequestHandler<GetSellerProfileQuery, SellerProfile>
{
    public async Task<SellerProfile> Handle(
        GetSellerProfileQuery request,
        CancellationToken cancellationToken)
    {
        var seller = await sellers.GetByIdAsync(request.SellerId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ขาย");
        return SellerProfile.From(seller);
    }
}

public sealed record SaveSellerPayoutAccountCommand(
    Guid SellerId,
    Guid? AccountId,
    string BankCode,
    string AccountName,
    string AccountNumber) : IRequest<SellerProfile>;

public sealed class SaveSellerPayoutAccountHandler(
    ISellerRepository sellers,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<SaveSellerPayoutAccountCommand, SellerProfile>
{
    public async Task<SellerProfile> Handle(
        SaveSellerPayoutAccountCommand request,
        CancellationToken cancellationToken)
    {
        var seller = await sellers.GetByIdAsync(request.SellerId, cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ขาย");
        var account = seller.SavePayoutAccount(
            request.AccountId,
            request.BankCode,
            request.AccountName,
            request.AccountNumber,
            clock.UtcNow);
        if (!request.AccountId.HasValue)
            await sellers.AddPayoutAccountAsync(account, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return SellerProfile.From(seller);
    }
}

public sealed record SellerPayoutAccountView(
    Guid Id,
    string BankCode,
    string AccountName,
    string MaskedNumber,
    bool IsDefault);

public sealed record SellerProfile(
    Guid Id,
    string PhoneNumber,
    string DisplayName,
    DateTimeOffset PhoneVerifiedAt,
    IReadOnlyList<SellerPayoutAccountView> PayoutAccounts)
{
    public static SellerProfile From(SellerAccount seller) =>
        new(
            seller.Id,
            seller.PhoneNumber,
            seller.DisplayName,
            seller.PhoneVerifiedAt,
            seller.PayoutAccounts
                .OrderByDescending(x => x.IsDefault)
                .ThenBy(x => x.CreatedAt)
                .Select(x => new SellerPayoutAccountView(
                    x.Id,
                    x.BankCode,
                    x.AccountName,
                    x.MaskedNumber,
                    x.IsDefault))
                .ToArray());
}
