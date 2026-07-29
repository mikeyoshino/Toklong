using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;

namespace Toklong.Application.Features.Buyers;

public sealed record BuyerEmailChangeView(
    Guid ChallengeId,
    string MaskedEmail,
    DateTimeOffset ExpiresAt,
    DateTimeOffset ResendAvailableAt,
    int RemainingAttempts);

public sealed record VerifiedBuyerEmailChangeView(
    string Email,
    DateTimeOffset CompletedAt);

public sealed record GetPendingBuyerEmailChangeQuery(Guid BuyerId)
    : IRequest<BuyerEmailChangeView?>;

public sealed record RequestBuyerEmailChangeCommand(
    Guid BuyerId,
    string Email,
    string IdempotencyKey)
    : IRequest<BuyerEmailChangeView>;

public sealed record ResendBuyerEmailChangeCommand(
    Guid BuyerId,
    Guid ChallengeId,
    string IdempotencyKey)
    : IRequest<BuyerEmailChangeView>;

public sealed record VerifyBuyerEmailChangeCommand(
    Guid BuyerId,
    Guid ChallengeId,
    string Code,
    string IdempotencyKey)
    : IRequest<VerifiedBuyerEmailChangeView>;

public sealed class GetPendingBuyerEmailChangeHandler(
    IBuyerEmailChangeRepository emailChanges,
    IClock clock)
    : IRequestHandler<
        GetPendingBuyerEmailChangeQuery,
        BuyerEmailChangeView?>
{
    public async Task<BuyerEmailChangeView?> Handle(
        GetPendingBuyerEmailChangeQuery request,
        CancellationToken cancellationToken)
    {
        var challenge = await emailChanges.GetOpenByBuyerIdAsync(
            request.BuyerId,
            cancellationToken);
        return challenge is
        {
            Status: BuyerEmailChangeStatus.Active
        } && challenge.ExpiresAt > clock.UtcNow
            ? BuyerEmailChangeOperations.ToView(challenge)
            : null;
    }
}

public sealed class RequestBuyerEmailChangeHandler(
    IBuyerRepository buyers,
    IBuyerEmailChangeRepository emailChanges,
    IEmailVerificationCodeService codes,
    IEmailVerificationTemplate template,
    ITransactionalEmailSender sender,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        RequestBuyerEmailChangeCommand,
        BuyerEmailChangeView>
{
    public async Task<BuyerEmailChangeView> Handle(
        RequestBuyerEmailChangeCommand request,
        CancellationToken cancellationToken)
    {
        var buyer = await buyers.GetByIdAsync(
                request.BuyerId,
                cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ซื้อ");
        var normalizedEmail = BuyerAccount.NormalizeEmail(request.Email);
        if (string.Equals(
                buyer.Email,
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase))
            throw new DomainException(
                "อีเมลนี้เป็นอีเมลปัจจุบันของคุณแล้ว");

        var requestKey =
            BuyerEmailChangeOperations.NormalizeIdempotencyKey(
                request.IdempotencyKey);
        var replay = await emailChanges.GetByRequestKeyAsync(
            request.BuyerId,
            requestKey,
            cancellationToken);
        if (replay is not null)
        {
            if (replay.SourceChallengeId is not null ||
                !string.Equals(
                    replay.PendingEmail,
                    normalizedEmail,
                    StringComparison.OrdinalIgnoreCase))
                throw BuyerEmailChangeOperations.NonExactReplay();
            return BuyerEmailChangeOperations.SuccessfulSendReplay(replay);
        }

        var open = await emailChanges.GetOpenByBuyerIdAsync(
            request.BuyerId,
            cancellationToken);
        if (open is not null)
            open.Supersede(clock.UtcNow);

        return await BuyerEmailChangeOperations.CreateAndSendAsync(
            request.BuyerId,
            normalizedEmail,
            requestKey,
            "account.email_change_requested",
            null,
            emailChanges,
            codes,
            template,
            sender,
            unitOfWork,
            clock,
            cancellationToken);
    }
}

public sealed class ResendBuyerEmailChangeHandler(
    IBuyerEmailChangeRepository emailChanges,
    IEmailVerificationCodeService codes,
    IEmailVerificationTemplate template,
    ITransactionalEmailSender sender,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        ResendBuyerEmailChangeCommand,
        BuyerEmailChangeView>
{
    public async Task<BuyerEmailChangeView> Handle(
        ResendBuyerEmailChangeCommand request,
        CancellationToken cancellationToken)
    {
        var original = await emailChanges.GetByIdAsync(
                request.ChallengeId,
                cancellationToken)
            ?? throw new NotFoundException(
                "ไม่พบคำขอเปลี่ยนอีเมล");
        BuyerEmailChangeOperations.EnsureOwnership(
            original,
            request.BuyerId);

        var requestKey =
            BuyerEmailChangeOperations.NormalizeIdempotencyKey(
                request.IdempotencyKey);
        var replay = await emailChanges.GetByRequestKeyAsync(
            request.BuyerId,
            requestKey,
            cancellationToken);
        if (replay is not null)
        {
            if (replay.SourceChallengeId != original.Id ||
                !string.Equals(
                    replay.PendingEmail,
                    original.PendingEmail,
                    StringComparison.OrdinalIgnoreCase))
                throw BuyerEmailChangeOperations.NonExactReplay();
            return BuyerEmailChangeOperations.SuccessfulSendReplay(replay);
        }

        if (original.Status == BuyerEmailChangeStatus.Active &&
            clock.UtcNow < original.ResendAvailableAt)
            throw new DomainException(
                "กรุณารอสักครู่ก่อนส่งรหัสอีกครั้ง");
        if (original.Status == BuyerEmailChangeStatus.Active &&
            original.ExpiresAt <= clock.UtcNow)
            throw new DomainException(
                "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่");

        original.EnsureCanResend(clock.UtcNow);
        original.Supersede(clock.UtcNow);
        return await BuyerEmailChangeOperations.CreateAndSendAsync(
            request.BuyerId,
            original.PendingEmail,
            requestKey,
            "account.email_change_code_resent",
            original.Id,
            emailChanges,
            codes,
            template,
            sender,
            unitOfWork,
            clock,
            cancellationToken);
    }
}

public sealed class VerifyBuyerEmailChangeHandler(
    IBuyerRepository buyers,
    IBuyerEmailChangeRepository emailChanges,
    IEmailVerificationCodeService codes,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        VerifyBuyerEmailChangeCommand,
        VerifiedBuyerEmailChangeView>
{
    public async Task<VerifiedBuyerEmailChangeView> Handle(
        VerifyBuyerEmailChangeCommand request,
        CancellationToken cancellationToken)
    {
        var challenge = await emailChanges.GetByIdAsync(
                request.ChallengeId,
                cancellationToken)
            ?? throw new NotFoundException(
                "ไม่พบคำขอเปลี่ยนอีเมล");
        BuyerEmailChangeOperations.EnsureOwnership(
            challenge,
            request.BuyerId);
        var verificationKey =
            BuyerEmailChangeOperations.NormalizeIdempotencyKey(
                request.IdempotencyKey);
        var submittedDigest = codes.Digest(
            challenge.Id,
            request.Code);
        var replay = await emailChanges.GetVerificationAttemptAsync(
            request.BuyerId,
            challenge.Id,
            verificationKey,
            cancellationToken);
        if (replay is not null)
            return BuyerEmailChangeOperations.ReplayVerification(
                challenge,
                replay,
                submittedDigest);

        var buyer = await buyers.GetByIdAsync(
                request.BuyerId,
                cancellationToken)
            ?? throw new NotFoundException("ไม่พบโปรไฟล์ผู้ซื้อ");

        BuyerEmailChangeOperations.EnsureVerifiableStatus(challenge);
        BuyerEmailVerificationOutcome outcome;
        try
        {
            outcome = challenge.Verify(
                submittedDigest,
                verificationKey,
                clock.UtcNow);
        }
        catch (DomainException) when (
            challenge.Status == BuyerEmailChangeStatus.Expired)
        {
            var expiredAttempt =
                BuyerEmailChangeOperations.VerificationAttempt(
                    challenge,
                    verificationKey,
                    submittedDigest,
                    BuyerEmailVerificationAttemptOutcome.Expired,
                    clock.UtcNow);
            await emailChanges.AddVerificationAttemptAsync(
                expiredAttempt,
                cancellationToken);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (
                emailChanges.IsPersistenceConflict(exception))
            {
                return await BuyerEmailChangeOperations
                    .RecoverVerificationConflictAsync(
                        request.BuyerId,
                        challenge.Id,
                        verificationKey,
                        submittedDigest,
                        emailChanges,
                        cancellationToken);
            }

            return BuyerEmailChangeOperations.ReplayVerification(
                challenge,
                expiredAttempt,
                submittedDigest);
        }

        if (outcome == BuyerEmailVerificationOutcome.ExactReplay)
        {
            return new VerifiedBuyerEmailChangeView(
                challenge.PendingEmail,
                challenge.VerifiedAt!.Value);
        }

        var attemptOutcome = outcome switch
        {
            BuyerEmailVerificationOutcome.Verified =>
                BuyerEmailVerificationAttemptOutcome.Verified,
            BuyerEmailVerificationOutcome.Incorrect =>
                BuyerEmailVerificationAttemptOutcome.Incorrect,
            BuyerEmailVerificationOutcome.Locked =>
                BuyerEmailVerificationAttemptOutcome.Locked,
            _ => throw new InvalidOperationException(
                "Unsupported email verification outcome.")
        };
        var attempt = BuyerEmailChangeOperations.VerificationAttempt(
            challenge,
            verificationKey,
            submittedDigest,
            attemptOutcome,
            clock.UtcNow);
        await emailChanges.AddVerificationAttemptAsync(
            attempt,
            cancellationToken);

        if (outcome == BuyerEmailVerificationOutcome.Locked)
        {
            await emailChanges.AddAuditAsync(
                BuyerEmailChangeOperations.Audit(
                    challenge,
                    "account.email_change_locked",
                    "locked",
                    codes,
                    clock.UtcNow),
                cancellationToken);
        }
        else if (outcome == BuyerEmailVerificationOutcome.Verified)
        {
            buyer.ActivateVerifiedEmail(challenge.PendingEmail);
            await emailChanges.AddAuditAsync(
                BuyerEmailChangeOperations.Audit(
                    challenge,
                    "account.email_change_verified",
                    "verified",
                    codes,
                    clock.UtcNow),
                cancellationToken);
        }

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            emailChanges.IsPersistenceConflict(exception))
        {
            return await BuyerEmailChangeOperations
                .RecoverVerificationConflictAsync(
                    request.BuyerId,
                    challenge.Id,
                    verificationKey,
                    submittedDigest,
                    emailChanges,
                    cancellationToken);
        }

        return BuyerEmailChangeOperations.ReplayVerification(
            challenge,
            attempt,
            submittedDigest);
    }
}

internal static class BuyerEmailChangeOperations
{
    private const string SenderError =
        "ยังส่งอีเมลไม่ได้ กรุณาลองอีกครั้ง";
    private const string MessagePurpose =
        "account.email_change_verification";

    public static async Task<BuyerEmailChangeView> CreateAndSendAsync(
        Guid buyerId,
        string normalizedEmail,
        string requestKey,
        string acceptedAuditName,
        Guid? sourceChallengeId,
        IBuyerEmailChangeRepository emailChanges,
        IEmailVerificationCodeService codes,
        IEmailVerificationTemplate template,
        ITransactionalEmailSender sender,
        IUnitOfWork unitOfWork,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var challengeId = Guid.NewGuid();
        var code = codes.Issue(challengeId);
        var challenge = BuyerEmailChangeChallenge.Create(
            challengeId,
            buyerId,
            normalizedEmail,
            Mask(normalizedEmail),
            code.Digest,
            requestKey,
            clock.UtcNow,
            sourceChallengeId);
        await emailChanges.AddAsync(challenge, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            emailChanges.IsPersistenceConflict(exception))
        {
            return await RecoverSendConflictAsync(
                buyerId,
                normalizedEmail,
                requestKey,
                sourceChallengeId,
                emailChanges,
                cancellationToken);
        }

        var rendered = template.Render(code.Code);
        try
        {
            await sender.SendAsync(
                new TransactionalEmailMessage(
                    normalizedEmail,
                    rendered.Subject,
                    rendered.TextBody,
                    rendered.HtmlBody,
                    MessagePurpose,
                    challenge.Id.ToString("N"),
                    challenge.Id.ToString("N")),
                cancellationToken);
        }
        catch (TransactionalEmailSendException exception)
        {
            challenge.MarkSendFailed(clock.UtcNow);
            await emailChanges.AddAuditAsync(
                Audit(
                    challenge,
                    "account.email_change_send_failed",
                    exception.Kind.ToString().ToLowerInvariant(),
                    codes,
                    clock.UtcNow),
                cancellationToken);
            try
            {
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch (Exception saveException) when (
                emailChanges.IsPersistenceConflict(saveException))
            {
                return await RecoverSendConflictAsync(
                    buyerId,
                    normalizedEmail,
                    requestKey,
                    sourceChallengeId,
                    emailChanges,
                    cancellationToken);
            }
            throw new DomainException(SenderError);
        }

        challenge.MarkSendAccepted(clock.UtcNow);
        await emailChanges.AddAuditAsync(
            Audit(
                challenge,
                acceptedAuditName,
                "accepted",
                codes,
                clock.UtcNow),
            cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            emailChanges.IsPersistenceConflict(exception))
        {
            return await RecoverSendConflictAsync(
                buyerId,
                normalizedEmail,
                requestKey,
                sourceChallengeId,
                emailChanges,
                cancellationToken);
        }
        return ToView(challenge);
    }

    public static BuyerEmailVerificationAttempt VerificationAttempt(
        BuyerEmailChangeChallenge challenge,
        string verificationKey,
        string submittedDigest,
        BuyerEmailVerificationAttemptOutcome outcome,
        DateTimeOffset createdAt) =>
        new(
            Guid.NewGuid(),
            challenge.BuyerId,
            challenge.Id,
            verificationKey,
            submittedDigest,
            outcome,
            challenge.RemainingAttempts,
            createdAt,
            outcome == BuyerEmailVerificationAttemptOutcome.Verified
                ? challenge.VerifiedAt
                : null);

    public static VerifiedBuyerEmailChangeView ReplayVerification(
        BuyerEmailChangeChallenge challenge,
        BuyerEmailVerificationAttempt attempt,
        string submittedDigest)
    {
        if (!string.Equals(
                attempt.SubmittedDigest,
                submittedDigest,
                StringComparison.OrdinalIgnoreCase))
            throw NonExactReplay();

        return attempt.Outcome switch
        {
            BuyerEmailVerificationAttemptOutcome.Verified =>
                new VerifiedBuyerEmailChangeView(
                    challenge.PendingEmail,
                    attempt.CompletedAt!.Value),
            BuyerEmailVerificationAttemptOutcome.Incorrect =>
                throw new DomainException(
                    "รหัสไม่ถูกต้อง ลองตรวจสอบแล้วกรอกอีกครั้ง"),
            BuyerEmailVerificationAttemptOutcome.Locked =>
                throw new DomainException(
                    "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่"),
            BuyerEmailVerificationAttemptOutcome.Expired =>
                throw new DomainException(
                    "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่"),
            _ => throw new InvalidOperationException(
                "Unsupported email verification attempt outcome.")
        };
    }

    public static async Task<VerifiedBuyerEmailChangeView>
        RecoverVerificationConflictAsync(
            Guid buyerId,
            Guid challengeId,
            string verificationKey,
            string submittedDigest,
            IBuyerEmailChangeRepository emailChanges,
            CancellationToken cancellationToken)
    {
        emailChanges.DiscardPendingChanges();
        var attempt =
            await emailChanges.GetVerificationAttemptAsync(
                buyerId,
                challengeId,
                verificationKey,
                cancellationToken);
        if (attempt is null)
            throw NonExactReplay();
        var challenge = await emailChanges.GetByIdAsync(
                challengeId,
                cancellationToken)
            ?? throw new NotFoundException(
                "ไม่พบคำขอเปลี่ยนอีเมล");
        EnsureOwnership(challenge, buyerId);
        return ReplayVerification(
            challenge,
            attempt,
            submittedDigest);
    }

    public static BuyerEmailChangeView ToView(
        BuyerEmailChangeChallenge challenge) =>
        new(
            challenge.Id,
            challenge.MaskedPendingEmail,
            challenge.ExpiresAt,
            challenge.ResendAvailableAt,
            challenge.RemainingAttempts);

    public static BuyerEmailChangeView SuccessfulSendReplay(
        BuyerEmailChangeChallenge challenge)
    {
        if (challenge.SendAcceptedAt is null)
            throw new DomainException(SenderError);
        return ToView(challenge);
    }

    private static async Task<BuyerEmailChangeView>
        RecoverSendConflictAsync(
            Guid buyerId,
            string normalizedEmail,
            string requestKey,
            Guid? sourceChallengeId,
            IBuyerEmailChangeRepository emailChanges,
            CancellationToken cancellationToken)
    {
        emailChanges.DiscardPendingChanges();
        var replay = await emailChanges.GetByRequestKeyAsync(
            buyerId,
            requestKey,
            cancellationToken);
        if (replay is null ||
            replay.SourceChallengeId != sourceChallengeId ||
            !string.Equals(
                replay.PendingEmail,
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase))
            throw NonExactReplay();
        return SuccessfulSendReplay(replay);
    }

    public static void EnsureOwnership(
        BuyerEmailChangeChallenge challenge,
        Guid buyerId)
    {
        if (challenge.BuyerId != buyerId)
            throw new ForbiddenException(
                "คุณไม่มีสิทธิ์เข้าถึงคำขอเปลี่ยนอีเมลนี้");
    }

    public static void EnsureVerifiableStatus(
        BuyerEmailChangeChallenge challenge)
    {
        switch (challenge.Status)
        {
            case BuyerEmailChangeStatus.Superseded:
                throw new DomainException(
                    "มีการส่งรหัสใหม่แล้ว กรุณาใช้รหัสล่าสุด");
            case BuyerEmailChangeStatus.Expired:
                throw new DomainException(
                    "รหัสหมดอายุแล้ว กรุณาขอรหัสใหม่");
            case BuyerEmailChangeStatus.Locked:
                throw new DomainException(
                    "กรอกรหัสไม่ถูกต้องครบจำนวนแล้ว กรุณาขอรหัสใหม่");
            case BuyerEmailChangeStatus.PendingSend:
            case BuyerEmailChangeStatus.SendFailed:
                throw new DomainException(SenderError);
        }
    }

    public static BuyerEmailChangeAuditEvent Audit(
        BuyerEmailChangeChallenge challenge,
        string name,
        string result,
        IEmailVerificationCodeService codes,
        DateTimeOffset createdAt) =>
        new(
            challenge.BuyerId,
            challenge.Id,
            name,
            codes.HashDestination(challenge.PendingEmail),
            challenge.MaskedPendingEmail,
            createdAt,
            result);

    public static string NormalizeIdempotencyKey(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new DomainException("รหัสคำขอไม่ถูกต้อง");
        return parsed.ToString("N");
    }

    public static DomainException NonExactReplay() =>
        new("คำขอนี้ไม่ตรงกับข้อมูลเดิม กรุณาลองใหม่");

    private static string Mask(string normalizedEmail)
    {
        var at = normalizedEmail.IndexOf('@');
        var local = normalizedEmail[..at];
        var revealedLength = Math.Min(2, local.Length);
        var maskLength = Math.Max(2, local.Length - revealedLength);
        return string.Concat(
            local.AsSpan(0, revealedLength),
            new string('•', maskLength),
            normalizedEmail.AsSpan(at));
    }
}
