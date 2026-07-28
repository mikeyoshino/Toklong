using Microsoft.EntityFrameworkCore;
using Toklong.Application.Abstractions;
using Toklong.Domain.Buyers;

namespace Toklong.Infrastructure.Persistence;

public sealed class BuyerEmailChangeRepository(
    ToklongDbContext dbContext)
    : IBuyerEmailChangeRepository
{
    public Task<BuyerEmailChangeChallenge?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges.SingleOrDefaultAsync(
            challenge => challenge.Id == id,
            cancellationToken);

    public Task<BuyerEmailChangeChallenge?> GetOpenByBuyerIdAsync(
        Guid buyerId,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges.SingleOrDefaultAsync(
            challenge =>
                challenge.BuyerId == buyerId &&
                (challenge.Status ==
                     BuyerEmailChangeStatus.PendingSend ||
                 challenge.Status ==
                     BuyerEmailChangeStatus.Active),
            cancellationToken);

    public Task<BuyerEmailChangeChallenge?> GetByRequestKeyAsync(
        Guid buyerId,
        string requestIdempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges.SingleOrDefaultAsync(
            challenge =>
                challenge.BuyerId == buyerId &&
                challenge.RequestIdempotencyKey ==
                    requestIdempotencyKey,
            cancellationToken);

    public Task AddAsync(
        BuyerEmailChangeChallenge challenge,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeChallenges
            .AddAsync(challenge, cancellationToken)
            .AsTask();

    public Task AddAuditAsync(
        BuyerEmailChangeAuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        dbContext.BuyerEmailChangeAuditEvents
            .AddAsync(auditEvent, cancellationToken)
            .AsTask();
}
