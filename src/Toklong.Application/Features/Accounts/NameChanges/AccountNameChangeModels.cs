using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Accounts;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;

namespace Toklong.Application.Features.Accounts.NameChanges;

public sealed record AccountNameChangeSubject(
    Guid? BuyerId,
    Guid? SellerId,
    Guid SessionId,
    string PhoneNumber);

public sealed record AccountNameChangeEligibility(
    bool CanChange,
    DateTimeOffset? NextAllowedAt);

public sealed record PendingAccountNameChange(
    Guid ChallengeId,
    string MaskedPhoneNumber,
    string FirstName,
    string LastName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

public static class AccountNameChangeCalendar
{
    private static readonly TimeZoneInfo Bangkok =
        TimeZoneInfo.FindSystemTimeZoneById("Asia/Bangkok");

    public static DateTimeOffset AddTwoBangkokCalendarMonths(
        DateTimeOffset changedAt)
    {
        var local = TimeZoneInfo.ConvertTime(changedAt, Bangkok);
        return local.AddMonths(2).ToUniversalTime();
    }
}

internal sealed record ResolvedAccountNameChangeSubject(
    AccountNameChangeSubject Subject,
    BuyerAccount? Buyer,
    SellerAccount? Seller,
    string PhoneNumber)
{
    public DateTimeOffset? LastNameChangedAt
    {
        get
        {
            DateTimeOffset? latest = null;
            foreach (var timestamp in new[]
            {
                Buyer?.NameChangedAt,
                Seller?.NameChangedAt
            })
            {
                if (timestamp.HasValue &&
                    (!latest.HasValue ||
                     timestamp.Value > latest.Value))
                    latest = timestamp;
            }

            return latest;
        }
    }

    public bool HasCurrentName(AccountName pendingName)
    {
        var names = new List<(string FirstName, string LastName)>();
        if (Buyer is not null)
            names.Add((Buyer.FirstName, Buyer.LastName));
        if (Seller is not null)
            names.Add((Seller.FirstName, Seller.LastName));
        return names.Count > 0 &&
               names.All(name =>
                   string.Equals(
                       name.FirstName,
                       pendingName.FirstName,
                       StringComparison.Ordinal) &&
                   string.Equals(
                       name.LastName,
                       pendingName.LastName,
                       StringComparison.Ordinal));
    }
}

internal static class AccountNameChangeSubjectResolver
{
    public static async Task<ResolvedAccountNameChangeSubject> ResolveAsync(
        AccountNameChangeSubject subject,
        IBuyerRepository buyers,
        ISellerRepository sellers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        if (!subject.BuyerId.HasValue && !subject.SellerId.HasValue)
            throw new ForbiddenException("บัญชีผู้ใช้ไม่ถูกต้อง");
        if (subject.BuyerId == Guid.Empty ||
            subject.SellerId == Guid.Empty ||
            subject.SessionId == Guid.Empty)
            throw new ForbiddenException("บัญชีผู้ใช้ไม่ถูกต้อง");

        string phone;
        try
        {
            phone = ThaiMobilePhone.Normalize(subject.PhoneNumber);
        }
        catch (ArgumentException)
        {
            throw new ForbiddenException("บัญชีผู้ใช้ไม่ถูกต้อง");
        }

        BuyerAccount? buyer = null;
        if (subject.BuyerId.HasValue)
        {
            buyer = await buyers.GetByIdAsync(
                    subject.BuyerId.Value,
                    cancellationToken)
                ?? throw new NotFoundException("ไม่พบบัญชีผู้ซื้อ");
            EnsurePhone(phone, buyer.PhoneNumber);
        }

        SellerAccount? seller = null;
        if (subject.SellerId.HasValue)
        {
            seller = await sellers.GetByIdAsync(
                    subject.SellerId.Value,
                    cancellationToken)
                ?? throw new NotFoundException("ไม่พบบัญชีผู้ขาย");
            EnsurePhone(phone, seller.PhoneNumber);
        }

        return new(subject, buyer, seller, phone);
    }

    public static void EnsureChallengeOwnership(
        AccountNameChangeChallenge challenge,
        AccountNameChangeSubject subject,
        string normalizedPhone)
    {
        EnsureSameAccountOwnership(
            challenge,
            subject,
            normalizedPhone);
        if (challenge.SessionId != subject.SessionId)
            throw new ForbiddenException(
                "คุณไม่มีสิทธิ์เข้าถึงคำขอเปลี่ยนชื่อนี้");
    }

    public static void EnsureSameAccountOwnership(
        AccountNameChangeChallenge challenge,
        AccountNameChangeSubject subject,
        string normalizedPhone)
    {
        var buyerConflict =
            challenge.BuyerId.HasValue &&
            challenge.BuyerId != subject.BuyerId;
        var sellerConflict =
            challenge.SellerId.HasValue &&
            challenge.SellerId != subject.SellerId;
        var sharesAttachedRole =
            (challenge.BuyerId.HasValue &&
             challenge.BuyerId == subject.BuyerId) ||
            (challenge.SellerId.HasValue &&
             challenge.SellerId == subject.SellerId);
        if (buyerConflict ||
            sellerConflict ||
            !sharesAttachedRole ||
            !string.Equals(
                challenge.PhoneNumber,
                normalizedPhone,
                StringComparison.Ordinal))
            throw new ForbiddenException(
                "คุณไม่มีสิทธิ์เข้าถึงคำขอเปลี่ยนชื่อนี้");
    }

    private static void EnsurePhone(string expected, string actual)
    {
        string normalizedActual;
        try
        {
            normalizedActual = ThaiMobilePhone.Normalize(actual);
        }
        catch (ArgumentException)
        {
            throw new ForbiddenException("บัญชีผู้ใช้ไม่ถูกต้อง");
        }

        if (!string.Equals(
                expected,
                normalizedActual,
                StringComparison.Ordinal))
            throw new ForbiddenException("บัญชีผู้ใช้ไม่ถูกต้อง");
    }
}

internal static class AccountNameChangeEligibilityPolicy
{
    public static AccountNameChangeEligibility Evaluate(
        ResolvedAccountNameChangeSubject subject,
        DateTimeOffset now)
    {
        if (!subject.LastNameChangedAt.HasValue)
            return new(true, null);

        var nextAllowedAt =
            AccountNameChangeCalendar.AddTwoBangkokCalendarMonths(
                subject.LastNameChangedAt.Value);
        return now >= nextAllowedAt
            ? new(true, null)
            : new(false, nextAllowedAt);
    }

    public static void EnsureEligible(
        ResolvedAccountNameChangeSubject subject,
        DateTimeOffset now)
    {
        var eligibility = Evaluate(subject, now);
        if (!eligibility.CanChange)
            throw new AccountNameChangeCooldownException(
                "ยังเปลี่ยนชื่อไม่ได้",
                eligibility.NextAllowedAt!.Value);
    }
}

public sealed class AccountNameChangeCooldownException(
    string message,
    DateTimeOffset nextAllowedAt) : Exception(message)
{
    public DateTimeOffset NextAllowedAt { get; } = nextAllowedAt;
}

internal static class AccountNameChangeViews
{
    public static PendingAccountNameChange ToPending(
        AccountNameChangeChallenge challenge)
    {
        if (challenge.Status != AccountNameChangeStatus.Active ||
            !challenge.ExpiresAt.HasValue ||
            !challenge.ResendAvailableAt.HasValue)
            throw new DomainException(
                "ยังส่งรหัสยืนยันไม่สำเร็จ กรุณาลองอีกครั้ง");

        return ToAcceptedSend(challenge);
    }

    public static PendingAccountNameChange ToAcceptedSend(
        AccountNameChangeChallenge challenge)
    {
        if (!challenge.SendAcceptedAt.HasValue ||
            !challenge.ExpiresAt.HasValue ||
            !challenge.ResendAvailableAt.HasValue)
            throw new DomainException(
                "ยังส่งรหัสยืนยันไม่สำเร็จ กรุณาลองอีกครั้ง");

        return new(
            challenge.Id,
            challenge.MaskedPhoneNumber,
            challenge.PendingFirstName,
            challenge.PendingLastName,
            challenge.ExpiresAt.Value,
            challenge.ResendAvailableAt.Value,
            challenge.RemainingAttempts);
    }
}
