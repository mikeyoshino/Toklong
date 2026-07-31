using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Accounts;
using Toklong.Domain.Common;

namespace Toklong.Application.Features.Accounts.NameChanges;

public sealed record RequestAccountNameChangeCommand(
    AccountNameChangeSubject Subject,
    string FirstName,
    string LastName,
    string IdempotencyKey)
    : IRequest<PendingAccountNameChange>;

public sealed class RequestAccountNameChangeHandler(
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IAccountNameChangeRepository nameChanges,
    IOtpVerificationProvider provider,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        RequestAccountNameChangeCommand,
        PendingAccountNameChange>
{
    public async Task<PendingAccountNameChange> Handle(
        RequestAccountNameChangeCommand request,
        CancellationToken cancellationToken)
    {
        var subject = await AccountNameChangeSubjectResolver.ResolveAsync(
            request.Subject,
            buyers,
            sellers,
            cancellationToken);
        var pendingName = AccountName.Create(
            request.FirstName,
            request.LastName);
        var requestKey =
            AccountNameChangeSendOperations.NormalizeIdempotencyKey(
                request.IdempotencyKey);

        var replay = await nameChanges.GetByRequestKeyAsync(
            subject.PhoneNumber,
            requestKey,
            cancellationToken);
        if (replay is not null)
        {
            AccountNameChangeSubjectResolver.EnsureChallengeOwnership(
                replay,
                request.Subject,
                subject.PhoneNumber);
            replay.EnsureExactOperationReplay(
                requestKey,
                null,
                pendingName);
            return await AccountNameChangeSendOperations
                .ReplayOrRecoverAsync(
                    replay,
                    provider,
                    nameChanges,
                    unitOfWork,
                    cancellationToken);
        }

        if (subject.HasCurrentName(pendingName))
            throw new DomainException(
                "ชื่อนี้เป็นชื่อปัจจุบันของคุณแล้ว");
        AccountNameChangeEligibilityPolicy.EnsureEligible(
            subject,
            clock.UtcNow);
        AccountNameChangeSendOperations.EnsureProviderCapabilities(provider);
        await AccountNameChangeSendOperations.EnsureDailyQuotaAsync(
            subject,
            nameChanges,
            clock.UtcNow,
            cancellationToken);

        var open = await nameChanges.GetOpenAsync(
            subject.PhoneNumber,
            cancellationToken);
        if (open is not null)
        {
            AccountNameChangeSubjectResolver.EnsureChallengeOwnership(
                open,
                request.Subject,
                subject.PhoneNumber);
            AccountNameChangeSendOperations.EnsureResendAvailable(
                open,
                clock.UtcNow);
            open.Supersede(clock.UtcNow);
        }

        return await AccountNameChangeSendOperations.CreateAndSendAsync(
            subject,
            pendingName,
            requestKey,
            null,
            provider,
            nameChanges,
            unitOfWork,
            clock,
            cancellationToken);
    }
}

internal static class AccountNameChangeSendOperations
{
    private const int MaximumAcceptedSendsPerDay = 5;
    private static readonly TimeSpan RollingWindow =
        TimeSpan.FromHours(24);
    private static readonly TimeSpan RequiredLifetime =
        TimeSpan.FromMinutes(10);
    private const string SendError =
        "ยังส่งรหัสยืนยันไม่สำเร็จ กรุณาลองอีกครั้ง";
    private const string UnknownOutcomeError =
        "กำลังตรวจสอบการส่งรหัส กรุณาลองอีกครั้ง";

    public static async Task<PendingAccountNameChange> CreateAndSendAsync(
        ResolvedAccountNameChangeSubject subject,
        AccountName pendingName,
        string requestKey,
        Guid? sourceChallengeId,
        IOtpVerificationProvider provider,
        IAccountNameChangeRepository nameChanges,
        IUnitOfWork unitOfWork,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var challenge = AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            subject.Subject.BuyerId,
            subject.Subject.SellerId,
            subject.Subject.SessionId,
            subject.PhoneNumber,
            Mask(subject.PhoneNumber),
            pendingName,
            requestKey,
            clock.UtcNow,
            sourceChallengeId);
        await nameChanges.AddAsync(challenge, cancellationToken);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            nameChanges.IsPersistenceConflict(exception))
        {
            return await RecoverPersistenceConflictAsync(
                subject,
                pendingName,
                requestKey,
                sourceChallengeId,
                provider,
                nameChanges,
                unitOfWork,
                clock.UtcNow,
                cancellationToken);
        }

        OtpChallenge acceptance;
        try
        {
            acceptance = await provider.RequestAsync(
                subject.PhoneNumber,
                OtpPurpose.AccountNameChange,
                challenge.ProviderRequestKey,
                cancellationToken);
        }
        catch (RequestCooldownException)
        {
            challenge.MarkSendFailed(clock.UtcNow);
            await SaveFailureAsync(
                challenge,
                unitOfWork,
                nameChanges,
                cancellationToken);
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DomainException(UnknownOutcomeError);
        }

        if (string.IsNullOrWhiteSpace(acceptance.ChallengeId))
        {
            challenge.MarkSendFailed(clock.UtcNow);
            await SaveFailureAsync(
                challenge,
                unitOfWork,
                nameChanges,
                cancellationToken);
            throw new DomainException(SendError);
        }

        challenge.MarkSendAccepted(
            acceptance.ChallengeId,
            clock.UtcNow);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            nameChanges.IsPersistenceConflict(exception))
        {
            nameChanges.DiscardPendingChanges();
            var stored = await nameChanges.GetByIdAsync(
                    challenge.Id,
                    cancellationToken)
                ?? throw new DomainException(UnknownOutcomeError);
            return await ReplayOrRecoverAsync(
                stored,
                provider,
                nameChanges,
                unitOfWork,
                cancellationToken);
        }

        return AccountNameChangeViews.ToPending(challenge);
    }

    public static async Task<PendingAccountNameChange> ReplayOrRecoverAsync(
        AccountNameChangeChallenge challenge,
        IOtpVerificationProvider provider,
        IAccountNameChangeRepository nameChanges,
        IUnitOfWork unitOfWork,
        CancellationToken cancellationToken)
    {
        if (challenge.SendAcceptedAt.HasValue)
            return AccountNameChangeViews.ToAcceptedSend(challenge);
        if (challenge.Status != AccountNameChangeStatus.PendingSend)
            throw new DomainException(SendError);

        EnsureProviderCapabilities(provider);
        OtpChallengeRecovery? recovery;
        try
        {
            recovery = await provider.LookupAsync(
                challenge.ProviderRequestKey,
                challenge.PhoneNumber,
                OtpPurpose.AccountNameChange,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new DomainException(UnknownOutcomeError);
        }

        if (recovery is null)
            throw new DomainException(UnknownOutcomeError);
        EnsureRecoveryMatches(challenge, recovery);
        challenge.MarkSendAccepted(
            recovery.Challenge.ChallengeId,
            recovery.AcceptedAt);
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            nameChanges.IsPersistenceConflict(exception))
        {
            nameChanges.DiscardPendingChanges();
            var stored = await nameChanges.GetByIdAsync(
                    challenge.Id,
                    cancellationToken)
                ?? throw new DomainException(UnknownOutcomeError);
            if (stored.Status != AccountNameChangeStatus.Active)
                throw new DomainException(UnknownOutcomeError);
            return AccountNameChangeViews.ToPending(stored);
        }

        return AccountNameChangeViews.ToPending(challenge);
    }

    public static async Task EnsureDailyQuotaAsync(
        ResolvedAccountNameChangeSubject subject,
        IAccountNameChangeRepository nameChanges,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var since = now - RollingWindow;
        var count = await nameChanges.CountAcceptedSendsAsync(
            subject.Subject.BuyerId,
            subject.Subject.SellerId,
            subject.PhoneNumber,
            since,
            cancellationToken);
        if (count < MaximumAcceptedSendsPerDay)
            return;

        var oldest = await nameChanges.GetOldestAcceptedSendAtAsync(
            subject.Subject.BuyerId,
            subject.Subject.SellerId,
            subject.PhoneNumber,
            since,
            cancellationToken);
        var retryAfter = oldest.HasValue
            ? oldest.Value.Add(RollingWindow) - now
            : RollingWindow;
        if (retryAfter <= TimeSpan.Zero)
            retryAfter = TimeSpan.FromSeconds(1);
        throw new RequestCooldownException(
            "ขอรหัสยืนยันครบจำนวนแล้ว กรุณาลองใหม่ภายหลัง",
            retryAfter);
    }

    public static void EnsureResendAvailable(
        AccountNameChangeChallenge challenge,
        DateTimeOffset now)
    {
        if (challenge.Status == AccountNameChangeStatus.PendingSend)
            throw new DomainException(UnknownOutcomeError);
        if (challenge.Status != AccountNameChangeStatus.Active)
            throw new DomainException(
                "รหัสยืนยันไม่อยู่ในสถานะที่ใช้งานได้");
        if (challenge.ExpiresAt <= now)
            throw new DomainException(
                "รหัสยืนยันหมดอายุแล้ว");
        if (challenge.ResendAvailableAt > now)
            throw new RequestCooldownException(
                "กรุณารอก่อนขอรหัสยืนยันอีกครั้ง",
                challenge.ResendAvailableAt.Value - now);
    }

    public static void EnsureProviderCapabilities(
        IOtpVerificationProvider provider)
    {
        var capabilities = provider.Capabilities;
        if (!capabilities.SupportsAccountNameChange ||
            capabilities.AccountNameChangeCodeLifetime != RequiredLifetime ||
            !capabilities.SupportsRequestLookup)
            throw new DomainException(
                "ยังไม่พร้อมส่งรหัสยืนยันสำหรับการเปลี่ยนชื่อ");
    }

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

    private static async Task<PendingAccountNameChange>
        RecoverPersistenceConflictAsync(
            ResolvedAccountNameChangeSubject subject,
            AccountName pendingName,
            string requestKey,
            Guid? sourceChallengeId,
            IOtpVerificationProvider provider,
            IAccountNameChangeRepository nameChanges,
            IUnitOfWork unitOfWork,
            DateTimeOffset now,
            CancellationToken cancellationToken)
    {
        nameChanges.DiscardPendingChanges();
        var replay = await nameChanges.GetByRequestKeyAsync(
            subject.PhoneNumber,
            requestKey,
            cancellationToken);
        if (replay is not null)
        {
            AccountNameChangeSubjectResolver.EnsureChallengeOwnership(
                replay,
                subject.Subject,
                subject.PhoneNumber);
            replay.EnsureExactOperationReplay(
                requestKey,
                sourceChallengeId,
                pendingName);
            return await ReplayOrRecoverAsync(
                replay,
                provider,
                nameChanges,
                unitOfWork,
                cancellationToken);
        }

        var open = await nameChanges.GetOpenAsync(
            subject.PhoneNumber,
            cancellationToken);
        if (open is not null)
        {
            AccountNameChangeSubjectResolver.EnsureChallengeOwnership(
                open,
                subject.Subject,
                subject.PhoneNumber);
            EnsureResendAvailable(open, now);
        }
        throw NonExactReplay();
    }

    private static async Task SaveFailureAsync(
        AccountNameChangeChallenge challenge,
        IUnitOfWork unitOfWork,
        IAccountNameChangeRepository nameChanges,
        CancellationToken cancellationToken)
    {
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (
            nameChanges.IsPersistenceConflict(exception))
        {
            nameChanges.DiscardPendingChanges();
            var stored = await nameChanges.GetByIdAsync(
                challenge.Id,
                cancellationToken);
            if (stored?.Status != AccountNameChangeStatus.SendFailed)
                throw new DomainException(UnknownOutcomeError);
        }
    }

    private static void EnsureRecoveryMatches(
        AccountNameChangeChallenge challenge,
        OtpChallengeRecovery recovery)
    {
        if (!string.Equals(
                recovery.ProviderRequestKey,
                challenge.ProviderRequestKey,
                StringComparison.Ordinal) ||
            recovery.Purpose != OtpPurpose.AccountNameChange ||
            !string.Equals(
                recovery.PhoneNumber,
                challenge.PhoneNumber,
                StringComparison.Ordinal) ||
            recovery.ExpiresAt != recovery.AcceptedAt.Add(RequiredLifetime) ||
            string.IsNullOrWhiteSpace(
                recovery.Challenge.ChallengeId))
            throw new DomainException(UnknownOutcomeError);
    }

    private static string Mask(string phone) =>
        $"0••-•••-{phone[^4..]}";
}
