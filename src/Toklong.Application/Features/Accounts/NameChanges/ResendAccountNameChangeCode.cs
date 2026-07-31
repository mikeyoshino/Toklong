using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Domain.Accounts;
using Toklong.Domain.Common;

namespace Toklong.Application.Features.Accounts.NameChanges;

public sealed record ResendAccountNameChangeCodeCommand(
    AccountNameChangeSubject Subject,
    Guid ChallengeId,
    string IdempotencyKey)
    : IRequest<PendingAccountNameChange>;

public sealed class ResendAccountNameChangeCodeHandler(
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IAccountNameChangeRepository nameChanges,
    IOtpVerificationProvider provider,
    IUnitOfWork unitOfWork,
    IClock clock)
    : IRequestHandler<
        ResendAccountNameChangeCodeCommand,
        PendingAccountNameChange>
{
    public async Task<PendingAccountNameChange> Handle(
        ResendAccountNameChangeCodeCommand request,
        CancellationToken cancellationToken)
    {
        var subject = await AccountNameChangeSubjectResolver.ResolveAsync(
            request.Subject,
            buyers,
            sellers,
            cancellationToken);
        var original = await nameChanges.GetByIdAsync(
                request.ChallengeId,
                cancellationToken)
            ?? throw new Toklong.Application.Common.NotFoundException(
                "ไม่พบคำขอเปลี่ยนชื่อ");
        AccountNameChangeSubjectResolver.EnsureChallengeOwnership(
            original,
            request.Subject,
            subject.PhoneNumber);
        var pendingName = AccountName.Create(
            original.PendingFirstName,
            original.PendingLastName);
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
                    original.Id,
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

        AccountNameChangeEligibilityPolicy.EnsureEligible(
            subject,
            clock.UtcNow);
        AccountNameChangeSendOperations.EnsureProviderCapabilities(provider);
        var priorResend =
            await nameChanges.GetBySourceChallengeIdAsync(
                original.Id,
                cancellationToken);
        if (priorResend is not null)
            throw AccountNameChangeSendOperations.NonExactReplay();

        var expired =
            original.Status == AccountNameChangeStatus.Active &&
            original.ExpiresAt <= clock.UtcNow;
        if (expired)
            original.Expire(clock.UtcNow);
        else
            AccountNameChangeSendOperations.EnsureResendAvailable(
                original,
                clock.UtcNow);
        await AccountNameChangeSendOperations.EnsureDailyQuotaAsync(
            subject,
            nameChanges,
            clock.UtcNow,
            cancellationToken);
        if (!expired)
            original.Supersede(clock.UtcNow);

        return await AccountNameChangeSendOperations.CreateAndSendAsync(
            subject,
            pendingName,
            requestKey,
            original.Id,
            provider,
            nameChanges,
            unitOfWork,
            clock,
            cancellationToken);
    }
}
