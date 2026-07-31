using MediatR;
using Toklong.Application.Abstractions;

namespace Toklong.Application.Features.Accounts.NameChanges;

public sealed record GetAccountNameChangeEligibilityQuery(
    AccountNameChangeSubject Subject)
    : IRequest<AccountNameChangeEligibility>;

public sealed class GetAccountNameChangeEligibilityHandler(
    IBuyerRepository buyers,
    ISellerRepository sellers,
    IClock clock)
    : IRequestHandler<
        GetAccountNameChangeEligibilityQuery,
        AccountNameChangeEligibility>
{
    public async Task<AccountNameChangeEligibility> Handle(
        GetAccountNameChangeEligibilityQuery request,
        CancellationToken cancellationToken)
    {
        var subject = await AccountNameChangeSubjectResolver.ResolveAsync(
            request.Subject,
            buyers,
            sellers,
            cancellationToken);
        return AccountNameChangeEligibilityPolicy.Evaluate(
            subject,
            clock.UtcNow);
    }
}
