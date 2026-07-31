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
        var pendingName = CreatePendingName(
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
            try
            {
                replay.EnsureExactOperationReplay(
                    requestKey,
                    null,
                    pendingName);
            }
            catch (DomainException)
            {
                throw new AccountNameChangeIdempotencyException();
            }
            return await AccountNameChangeSendOperations
                .ReplayOrRecoverAsync(
                    replay,
                    provider,
                    nameChanges,
                    unitOfWork,
                    cancellationToken);
        }

        if (subject.HasCurrentName(pendingName))
            throw new AccountNameChangeUnchangedNameException();
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
            if (open.Status == AccountNameChangeStatus.Active &&
                open.ExpiresAt <= clock.UtcNow)
            {
                AccountNameChangeSubjectResolver
                    .EnsureSameAccountOwnership(
                        open,
                        request.Subject,
                        subject.PhoneNumber);
                open.Expire(clock.UtcNow);
            }
            else
            {
                AccountNameChangeSubjectResolver
                    .EnsureChallengeOwnership(
                        open,
                        request.Subject,
                        subject.PhoneNumber);
                AccountNameChangeSendOperations
                    .EnsureResendAvailable(
                        open,
                        clock.UtcNow);
                open.Supersede(clock.UtcNow);
            }
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

    private static AccountName CreatePendingName(
        string firstName,
        string lastName)
    {
        try
        {
            return AccountName.Create(firstName, lastName);
        }
        catch (DomainException)
        {
            try
            {
                _ = AccountName.Create(firstName, "ทดสอบ");
            }
            catch (DomainException exception)
            {
                throw new AccountNameChangeInputException(
                    AccountNameInputField.FirstName,
                    exception.Message);
            }

            throw new AccountNameChangeInputException(
                AccountNameInputField.LastName,
                "นามสกุลไม่ถูกต้อง");
        }
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
        catch (RequestCooldownException exception)
        {
            challenge.MarkSendFailed(
                clock.UtcNow,
                exception.Code,
                exception.Message,
                exception.RetryAfter);
            await SaveFailureAsync(
                challenge,
                unitOfWork,
                nameChanges,
                cancellationToken);
            throw new AccountNameChangeProviderThrottleException(
                exception.RetryAfter);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AccountNameChangeProviderOutcomeUnknownException();
        }

        if (string.IsNullOrWhiteSpace(acceptance.ChallengeId))
        {
            challenge.MarkSendFailed(
                clock.UtcNow,
                "otp_provider_invalid_response",
                SendError);
            await SaveFailureAsync(
                challenge,
                unitOfWork,
                nameChanges,
                cancellationToken);
            throw new AccountNameChangeProviderUnavailableException();
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
                ?? throw new AccountNameChangeProviderOutcomeUnknownException();
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
        if (challenge.Status == AccountNameChangeStatus.SendFailed)
            throw ReplaySendFailure(challenge);
        if (challenge.Status != AccountNameChangeStatus.PendingSend)
            throw new AccountNameChangeProviderUnavailableException();

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
            throw new AccountNameChangeProviderOutcomeUnknownException();
        }

        if (recovery is null)
            throw new AccountNameChangeProviderOutcomeUnknownException();
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
                ?? throw new AccountNameChangeProviderOutcomeUnknownException();
            if (stored.Status != AccountNameChangeStatus.Active)
                throw new AccountNameChangeProviderOutcomeUnknownException();
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
            retryAfter,
            "name_change_send_limit");
    }

    public static void EnsureResendAvailable(
        AccountNameChangeChallenge challenge,
        DateTimeOffset now)
    {
        if (challenge.Status == AccountNameChangeStatus.PendingSend)
            throw new AccountNameChangeProviderOutcomeUnknownException();
        if (challenge.Status != AccountNameChangeStatus.Active)
            throw new AccountNameChangeVerificationException(
                AccountNameVerificationFailure.Inactive);
        if (challenge.ExpiresAt <= now)
            throw new AccountNameChangeVerificationException(
                AccountNameVerificationFailure.Expired);
        if (challenge.ResendAvailableAt > now)
            throw new RequestCooldownException(
                "กรุณารอก่อนขอรหัสยืนยันอีกครั้ง",
                challenge.ResendAvailableAt.Value - now,
                "name_change_resend_cooldown");
    }

    public static void EnsureProviderCapabilities(
        IOtpVerificationProvider provider)
    {
        var capabilities = provider.Capabilities;
        if (!capabilities.SupportsAccountNameChange ||
            capabilities.AccountNameChangeCodeLifetime != RequiredLifetime ||
            !capabilities.SupportsRequestLookup)
            throw new AccountNameChangeProviderUnavailableException();
    }

    public static string NormalizeIdempotencyKey(string value)
    {
        var clean = (value ?? "").Trim();
        if (clean.Length != 32 ||
            !Guid.TryParseExact(clean, "N", out var parsed))
            throw new AccountNameChangeIdempotencyException();
        return parsed.ToString("N");
    }

    public static AccountNameChangeIdempotencyException NonExactReplay() =>
        new();

    private static Exception ReplaySendFailure(
        AccountNameChangeChallenge challenge)
    {
        var message = string.IsNullOrWhiteSpace(
            challenge.SendFailureMessage)
            ? SendError
            : challenge.SendFailureMessage;
        if (challenge.SendFailureRetryAfterTicks is not { } ticks ||
            ticks <= 0 ||
            ticks > TimeSpan.FromHours(24).Ticks)
            return new AccountNameChangeProviderUnavailableException();

        if (!string.Equals(
                challenge.SendFailureCode,
                "name_change_send_limit",
                StringComparison.Ordinal) &&
            !string.Equals(
                challenge.SendFailureCode,
                "name_change_resend_cooldown",
                StringComparison.Ordinal))
            return new AccountNameChangeProviderThrottleException(
                TimeSpan.FromTicks(ticks));

        return new RequestCooldownException(
            message,
            TimeSpan.FromTicks(ticks),
            challenge.SendFailureCode ?? "request_cooldown");
    }

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
                throw new AccountNameChangeProviderOutcomeUnknownException();
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
            throw new AccountNameChangeProviderOutcomeUnknownException();
    }

    private static string Mask(string phone) =>
        $"0••-•••-{phone[^4..]}";
}
