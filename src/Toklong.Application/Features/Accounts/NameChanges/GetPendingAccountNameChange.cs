using MediatR;
using Toklong.Application.Abstractions;
using Toklong.Application.Common;
using Toklong.Domain.Accounts;

namespace Toklong.Application.Features.Accounts.NameChanges;

public sealed record GetPendingAccountNameChangeQuery(
    AccountNameChangeSubject Subject)
    : IRequest<PendingAccountNameChange?>;

public sealed class GetPendingAccountNameChangeHandler(
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IAccountNameChangeRepository nameChanges,
    IClock clock)
    : IRequestHandler<
        GetPendingAccountNameChangeQuery,
        PendingAccountNameChange?>
{
    public async Task<PendingAccountNameChange?> Handle(
        GetPendingAccountNameChangeQuery request,
        CancellationToken cancellationToken)
    {
        ResolvedAccountNameChangeSubject subject;
        try
        {
            subject = await AccountNameChangeSubjectResolver.ResolveAsync(
                request.Subject,
                buyers,
                sellers,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is ForbiddenException or NotFoundException)
        {
            return null;
        }

        var challenge = await nameChanges.GetOpenAsync(
            subject.PhoneNumber,
            cancellationToken);
        if (challenge is null ||
            challenge.Status != AccountNameChangeStatus.Active ||
            challenge.ExpiresAt <= clock.UtcNow)
            return null;

        try
        {
            AccountNameChangeSubjectResolver.EnsureChallengeOwnership(
                challenge,
                request.Subject,
                subject.PhoneNumber);
        }
        catch (ForbiddenException)
        {
            return null;
        }

        return AccountNameChangeViews.ToPending(challenge);
    }
}
