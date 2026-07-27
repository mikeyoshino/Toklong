using Microsoft.EntityFrameworkCore;
using Toklong.Domain.Authentication;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Authentication;

public sealed class PendingRegistrationPersistenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 28, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Model_has_unique_ticket_hash_and_account_acceptance_idempotency()
    {
        using var database = CreateDatabase();
        var pending = database.Model
            .FindEntityType(typeof(PendingMobileRegistration))!;
        Assert.Contains(
            pending.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Single().Name ==
                     nameof(PendingMobileRegistration.TicketHash));

        var acceptance = database.Model
            .FindEntityType(typeof(MobileAccountTermsAcceptance))!;
        Assert.Contains(
            acceptance.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(MobileAccountTermsAcceptance.BuyerId),
                             nameof(MobileAccountTermsAcceptance.TermsVersion)
                         ]));
        Assert.Contains(
            acceptance.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Single().Name ==
                     nameof(MobileAccountTermsAcceptance.IdempotencyKey));
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Account_terms_acceptance_cannot_be_changed(
        EntityState forbiddenState)
    {
        await using var database = CreateDatabase();
        database.MobileAccountTermsAcceptances.Add(NewAcceptance());
        await database.SaveChangesAsync();
        var acceptance =
            database.MobileAccountTermsAcceptances.Single();
        database.Entry(acceptance).State = forbiddenState;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.SaveChangesAsync());
    }

    [Fact]
    public async Task Repository_finds_ticket_and_cleans_only_expired_rows()
    {
        await using var database = CreateDatabase();
        var repository =
            new PendingMobileRegistrationRepository(database);
        var expired = NewPending(
            new string('a', 64),
            Now.AddHours(-2),
            Now.AddHours(-1));
        var active = NewPending(
            new string('b', 64),
            Now,
            Now.AddMinutes(15));
        await repository.AddAsync(expired, default);
        await repository.AddAsync(active, default);
        await database.SaveChangesAsync();

        var found = await repository.GetByTicketHashAsync(
            active.TicketHash,
            default);
        var deleted = await repository.DeleteExpiredBeforeAsync(
            Now,
            default);

        Assert.Same(active, found);
        Assert.Equal(1, deleted);
        Assert.Equal(
            [active.Id],
            await database.PendingMobileRegistrations
                .Select(item => item.Id)
                .ToListAsync());
    }

    private static MobileAccountTermsAcceptance NewAcceptance() =>
        MobileAccountTermsAcceptance.Create(
            Guid.NewGuid(),
            "terms-mvp-v1",
            Guid.NewGuid().ToString("N"),
            Guid.NewGuid().ToString("N"),
            Now);

    private static PendingMobileRegistration NewPending(
        string ticketHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt) =>
        PendingMobileRegistration.Create(
            ticketHash,
            "+66812345678",
            Guid.NewGuid().ToString("N"),
            createdAt,
            expiresAt);

    private static ToklongDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new ToklongDbContext(options);
    }
}
