using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Common;

namespace Toklong.Application.Features.Accounts.NameChanges;

public sealed record VerifyAccountNameChangeCommand(
    AccountNameChangeSubject Subject,
    Guid ChallengeId,
    string Code,
    string IdempotencyKey)
    : IRequest<VerifiedAccountNameChange>;

public sealed record VerifiedAccountNameChange(
    string FirstName,
    string LastName,
    string DisplayName,
    DateTimeOffset CompletedAt);

public sealed class VerifyAccountNameChangeHandler(
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IMobileSessionRepository sessions,
    IAccountNameChangeRepository nameChanges,
    IOtpVerificationProvider provider,
    IAccountNameVerificationSecurity security,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        VerifyAccountNameChangeCommand,
        VerifiedAccountNameChange>
{
    private const int MaximumPersistenceAttempts = 5;
    private const string IncorrectCodeMessage =
        "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง";
    private const string LockedCodeMessage =
        "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่";
    private const string ExpiredCodeMessage =
        "รหัสยืนยันหมดอายุแล้ว กรุณาขอรหัสใหม่";
    private const string InactiveCodeMessage =
        "รหัสยืนยันไม่อยู่ในสถานะที่ใช้งานได้";
    private const string NonExactReplayMessage =
        "คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่";

    public async Task<VerifiedAccountNameChange> Handle(
        VerifyAccountNameChangeCommand request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var code = NormalizeCode(request.Code);
        var verificationKey =
            NormalizeIdempotencyKey(request.IdempotencyKey);
        if (request.ChallengeId == Guid.Empty)
            throw new DomainException(
                "รหัสคำขอเปลี่ยนชื่อไม่ถูกต้อง");
        var submittedDigest =
            security.Digest(request.ChallengeId, code);
        var providerInvoked = false;
        string? providerPhone = null;

        for (var persistenceAttempt = 0;
             persistenceAttempt < MaximumPersistenceAttempts;
             persistenceAttempt++)
        {
            if (persistenceAttempt > 0)
                nameChanges.DiscardPendingChanges();

            var now = clock.UtcNow;
            var subject = await ResolveAllRolesAsync(
                request.Subject,
                cancellationToken);
            await EnsureAuthenticatedSessionAsync(
                request.Subject,
                subject.PhoneNumber,
                now,
                cancellationToken);
            var challenge = await nameChanges.GetByIdAsync(
                    request.ChallengeId,
                    cancellationToken)
                ?? throw new NotFoundException(
                    "ไม่พบคำขอเปลี่ยนชื่อ");
            AccountNameChangeSubjectResolver.EnsureChallengeOwnership(
                challenge,
                request.Subject,
                subject.PhoneNumber);

            var existingAttempt = await nameChanges.GetAttemptAsync(
                challenge.Id,
                verificationKey,
                cancellationToken);
            if (existingAttempt is not null)
            {
                return Replay(
                    challenge,
                    existingAttempt,
                    submittedDigest);
            }

            EnsureChallengeCanBeSubmitted(challenge);
            AccountNameChangeEligibilityPolicy.EnsureEligible(
                subject,
                now);

            AccountNameVerificationOutcome outcome;
            if (challenge.ExpiresAt <= now)
            {
                outcome = challenge.RecordVerification(
                    verificationKey,
                    providerAccepted: false,
                    now);
            }
            else
            {
                if (!providerInvoked)
                {
                    providerPhone = await provider.VerifyAsync(
                        challenge.ProviderChallengeId!,
                        code,
                        OtpPurpose.AccountNameChange,
                        cancellationToken);
                    providerInvoked = true;
                }

                outcome = challenge.RecordVerification(
                    verificationKey,
                    ProviderPhoneMatches(
                        providerPhone,
                        subject.PhoneNumber),
                    now);
            }

            var attempt = CreateAttempt(
                subject,
                challenge,
                verificationKey,
                submittedDigest,
                outcome,
                now);
            await nameChanges.AddAttemptAsync(
                attempt,
                cancellationToken);

            if (outcome == AccountNameVerificationOutcome.Verified)
            {
                var pendingName = AccountName.Create(
                    challenge.PendingFirstName,
                    challenge.PendingLastName);
                var oldNameReference = security.DigestAuditValue(
                    challenge.Id,
                    $"{subject.Buyer?.FullName ?? ""}|" +
                    $"{subject.Seller?.DisplayName ?? ""}");
                var newNameReference = security.DigestAuditValue(
                    challenge.Id,
                    pendingName.DisplayName);
                subject.Buyer?.ApplyAccountName(pendingName, now);
                subject.Seller?.ApplyAccountName(pendingName, now);
                var activeSessions =
                    await sessions.GetActiveByPartyAsync(
                        subject.Buyer?.Id,
                        subject.Seller?.Id,
                        now,
                        cancellationToken);
                foreach (var session in activeSessions.Where(
                             value => HasPhone(
                                 value,
                                 subject.PhoneNumber)))
                    session.UpdateDisplayName(
                        pendingName.DisplayName);
                await nameChanges.AddAuditAsync(
                    new AccountNameChangeAuditEvent(
                        subject.Buyer?.Id,
                        subject.Seller?.Id,
                        request.Subject.SessionId,
                        challenge.Id,
                        oldNameReference,
                        newNameReference,
                        now,
                        "account.name_change_verified",
                        "verified"),
                    cancellationToken);
            }

            try
            {
                await unitOfWork.SaveChangesAsync(
                    cancellationToken);
            }
            catch (Exception exception) when (
                nameChanges.IsPersistenceConflict(exception))
            {
                continue;
            }

            return Replay(
                challenge,
                attempt,
                submittedDigest);
        }

        nameChanges.DiscardPendingChanges();
        var winningChallenge = await nameChanges.GetByIdAsync(
                request.ChallengeId,
                cancellationToken)
            ?? throw new NotFoundException(
                "ไม่พบคำขอเปลี่ยนชื่อ");
        var winningAttempt = await nameChanges.GetAttemptAsync(
            request.ChallengeId,
            verificationKey,
            cancellationToken);
        if (winningAttempt is not null)
            return Replay(
                winningChallenge,
                winningAttempt,
                submittedDigest);

        var currentSubject = await ResolveAllRolesAsync(
            request.Subject,
            cancellationToken);
        AccountNameChangeEligibilityPolicy.EnsureEligible(
            currentSubject,
            clock.UtcNow);
        throw new DomainException(
            "ยังยืนยันการเปลี่ยนชื่อไม่สำเร็จ กรุณาลองใหม่");
    }

    private async Task<ResolvedAccountNameChangeSubject>
        ResolveAllRolesAsync(
            AccountNameChangeSubject subject,
            CancellationToken cancellationToken)
    {
        var resolved =
            await AccountNameChangeSubjectResolver.ResolveAsync(
                subject,
                buyers,
                sellers,
                cancellationToken);
        var buyer = resolved.Buyer ??
            await buyers.GetByPhoneAsync(
                resolved.PhoneNumber,
                cancellationToken);
        var seller = resolved.Seller ??
            await sellers.GetByPhoneAsync(
                resolved.PhoneNumber,
                cancellationToken);
        return new(
            subject,
            buyer,
            seller,
            resolved.PhoneNumber);
    }

    private async Task EnsureAuthenticatedSessionAsync(
        AccountNameChangeSubject subject,
        string phoneNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var session = await sessions.GetByIdAsync(
                subject.SessionId,
                cancellationToken)
            ?? throw new ForbiddenException(
                "เซสชันไม่ถูกต้อง");
        var sharesAttachedRole =
            subject.BuyerId.HasValue &&
            session.BuyerId == subject.BuyerId ||
            subject.SellerId.HasValue &&
            session.SellerId == subject.SellerId;
        var buyerConflict =
            subject.BuyerId.HasValue &&
            session.BuyerId != subject.BuyerId;
        var sellerConflict =
            subject.SellerId.HasValue &&
            session.SellerId != subject.SellerId;
        if (!session.IsActive(now) ||
            buyerConflict ||
            sellerConflict ||
            !sharesAttachedRole ||
            !HasPhone(session, phoneNumber))
            throw new ForbiddenException(
                "เซสชันไม่ถูกต้อง");
    }

    private static void EnsureChallengeCanBeSubmitted(
        AccountNameChangeChallenge challenge)
    {
        if (challenge.Status == AccountNameChangeStatus.Locked)
            throw new DomainException(LockedCodeMessage);
        if (challenge.Status == AccountNameChangeStatus.Expired)
            throw new DomainException(ExpiredCodeMessage);
        if (challenge.Status != AccountNameChangeStatus.Active ||
            string.IsNullOrWhiteSpace(
                challenge.ProviderChallengeId))
            throw new DomainException(InactiveCodeMessage);
    }

    private static AccountNameVerificationAttempt CreateAttempt(
        ResolvedAccountNameChangeSubject subject,
        AccountNameChangeChallenge challenge,
        string verificationKey,
        string submittedDigest,
        AccountNameVerificationOutcome outcome,
        DateTimeOffset now)
    {
        var attemptOutcome = outcome switch
        {
            AccountNameVerificationOutcome.Verified =>
                AccountNameVerificationAttemptOutcome.Verified,
            AccountNameVerificationOutcome.Incorrect =>
                AccountNameVerificationAttemptOutcome.Incorrect,
            AccountNameVerificationOutcome.Locked =>
                AccountNameVerificationAttemptOutcome.Locked,
            AccountNameVerificationOutcome.Expired =>
                AccountNameVerificationAttemptOutcome.Expired,
            _ => throw new InvalidOperationException(
                "Unsupported account name verification outcome.")
        };
        return new(
            Guid.NewGuid(),
            subject.Buyer?.Id,
            subject.Seller?.Id,
            subject.Subject.SessionId,
            challenge.Id,
            verificationKey,
            submittedDigest,
            attemptOutcome,
            challenge.RemainingAttempts,
            now,
            attemptOutcome ==
                AccountNameVerificationAttemptOutcome.Verified
                ? challenge.VerifiedAt
                : null);
    }

    private static VerifiedAccountNameChange Replay(
        AccountNameChangeChallenge challenge,
        AccountNameVerificationAttempt attempt,
        string submittedDigest)
    {
        if (!string.Equals(
                attempt.SubmittedDigest,
                submittedDigest,
                StringComparison.OrdinalIgnoreCase))
            throw new DomainException(NonExactReplayMessage);

        return attempt.Outcome switch
        {
            AccountNameVerificationAttemptOutcome.Verified =>
                new(
                    challenge.PendingFirstName,
                    challenge.PendingLastName,
                    $"{challenge.PendingFirstName} " +
                    challenge.PendingLastName,
                    attempt.CompletedAt!.Value),
            AccountNameVerificationAttemptOutcome.Incorrect =>
                throw new DomainException(
                    IncorrectCodeMessage),
            AccountNameVerificationAttemptOutcome.Locked =>
                throw new DomainException(
                    LockedCodeMessage),
            AccountNameVerificationAttemptOutcome.Expired =>
                throw new DomainException(
                    ExpiredCodeMessage),
            _ => throw new InvalidOperationException(
                "Unsupported account name verification outcome.")
        };
    }

    private static bool ProviderPhoneMatches(
        string? providerPhone,
        string expectedPhone)
    {
        if (string.IsNullOrWhiteSpace(providerPhone))
            return false;
        try
        {
            return string.Equals(
                ThaiMobilePhone.Normalize(providerPhone),
                expectedPhone,
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static bool HasPhone(
        MobileSession session,
        string expectedPhone)
    {
        try
        {
            return string.Equals(
                ThaiMobilePhone.Normalize(session.PhoneNumber),
                expectedPhone,
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static string NormalizeCode(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 6 ||
            clean.Any(character => !char.IsAsciiDigit(character)))
            throw new DomainException(
                "กรุณากรอกรหัสยืนยัน 6 หลัก");
        return clean;
    }

    private static string NormalizeIdempotencyKey(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new DomainException("รหัสคำขอไม่ถูกต้อง");
        return parsed.ToString("N");
    }
}
