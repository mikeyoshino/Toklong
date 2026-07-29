using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Toklong.Domain.Buyers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Buyers;

public sealed class BuyerEmailChangePersistenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Model_has_one_pending_or_active_challenge_per_buyer()
    {
        using var database = CreateDatabase();
        var entity = database.Model.FindEntityType(
            typeof(BuyerEmailChangeChallenge))!;
        var index = Assert.Single(
            entity.GetIndexes(),
            value => value.IsUnique &&
                     value.Properties.Count == 1 &&
                     value.Properties.Single().Name ==
                         nameof(BuyerEmailChangeChallenge.BuyerId));

        Assert.Contains("PendingSend", index.GetFilter());
        Assert.Contains("Active", index.GetFilter());
    }

    [Fact]
    public void Model_has_required_lengths_concurrency_and_lookup_indexes()
    {
        using var database = CreateDatabase();
        var challenge = database.Model.FindEntityType(
            typeof(BuyerEmailChangeChallenge))!;
        var audit = database.Model.FindEntityType(
            typeof(BuyerEmailChangeAuditEvent))!;

        Assert.Equal(
            24,
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.Status))!.GetMaxLength());
        Assert.Equal(
            254,
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.PendingEmail))!.GetMaxLength());
        Assert.Equal(
            254,
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.MaskedPendingEmail))!.GetMaxLength());
        Assert.Equal(
            64,
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.CodeDigest))!.GetMaxLength());
        Assert.Equal(
            32,
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.RequestIdempotencyKey))!.GetMaxLength());
        Assert.Equal(
            32,
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.VerificationIdempotencyKey))!.GetMaxLength());
        Assert.True(
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.Version))!.IsConcurrencyToken);
        Assert.True(
            challenge.FindProperty(
                nameof(BuyerEmailChangeChallenge.SourceChallengeId))!.IsNullable);
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(BuyerEmailChangeChallenge.BuyerId),
                             nameof(BuyerEmailChangeChallenge.RequestIdempotencyKey)
                         ]));
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.Properties.Single().Name ==
                     nameof(BuyerEmailChangeChallenge.ExpiresAt));

        Assert.Equal(
            64,
            audit.FindProperty(
                nameof(BuyerEmailChangeAuditEvent.DestinationHash))!.GetMaxLength());
        Assert.Equal(
            254,
            audit.FindProperty(
                nameof(BuyerEmailChangeAuditEvent.MaskedDestination))!.GetMaxLength());
        Assert.Contains(
            audit.GetIndexes(),
            index => index.Properties.Single().Name ==
                     nameof(BuyerEmailChangeAuditEvent.ChallengeId));
        Assert.Equal(
            DeleteBehavior.Restrict,
            challenge.GetForeignKeys().Single().DeleteBehavior);
        Assert.Equal(
            DeleteBehavior.Restrict,
            audit.GetForeignKeys()
                .Single(foreignKey =>
                    foreignKey.Properties.Single().Name ==
                    nameof(BuyerEmailChangeAuditEvent.BuyerId))
                .DeleteBehavior);
    }

    [Fact]
    public async Task Repository_returns_challenges_by_supported_keys()
    {
        await using var database = CreateDatabase();
        var repository = new BuyerEmailChangeRepository(database);
        var active = ActiveChallenge(Now);
        await repository.AddAsync(active, default);
        await database.SaveChangesAsync();

        var byId = await repository.GetByIdAsync(active.Id, default);
        var byRequestKey = await repository.GetByRequestKeyAsync(
            active.BuyerId,
            active.RequestIdempotencyKey,
            default);
        var open = await repository.GetOpenByBuyerIdAsync(
            active.BuyerId,
            default);

        Assert.Equal(active.Id, byId?.Id);
        Assert.Equal(active.Id, byRequestKey?.Id);
        Assert.Equal(active.Id, open?.Id);
    }

    [Fact]
    public async Task Repository_returns_pending_send_as_open()
    {
        await using var database = CreateDatabase();
        var repository = new BuyerEmailChangeRepository(database);
        var pending = NewChallenge(Now);
        await repository.AddAsync(pending, default);
        await database.SaveChangesAsync();

        var found = await repository.GetOpenByBuyerIdAsync(
            pending.BuyerId,
            default);

        Assert.Equal(pending.Id, found?.Id);
    }

    [Fact]
    public async Task Repository_round_trips_resend_source_challenge()
    {
        await using var database = CreateDatabase();
        var repository = new BuyerEmailChangeRepository(database);
        var sourceId = Guid.NewGuid();
        var replacement = BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "bu•••@example.com",
            new string('a', 64),
            Guid.NewGuid().ToString("N"),
            Now,
            sourceId);
        await repository.AddAsync(replacement, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var stored = await repository.GetByIdAsync(
            replacement.Id,
            default);

        Assert.Equal(sourceId, stored?.SourceChallengeId);
    }

    [Fact]
    public async Task Repository_does_not_return_closed_challenge_as_open()
    {
        await using var database = CreateDatabase();
        var repository = new BuyerEmailChangeRepository(database);
        var failed = NewChallenge(Now);
        failed.MarkSendFailed(Now.AddSeconds(1));
        await repository.AddAsync(failed, default);
        await database.SaveChangesAsync();

        var found = await repository.GetOpenByBuyerIdAsync(
            failed.BuyerId,
            default);

        Assert.Null(found);
    }

    [Fact]
    public async Task Repository_adds_email_change_audit()
    {
        await using var database = CreateDatabase();
        var repository = new BuyerEmailChangeRepository(database);
        var challenge = NewChallenge(Now);
        var audit = NewAudit(challenge);
        await repository.AddAsync(challenge, default);
        await repository.AddAuditAsync(audit, default);
        await database.SaveChangesAsync();

        Assert.Equal(
            audit.ChallengeId,
            database.BuyerEmailChangeAuditEvents.Single().ChallengeId);
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Email_change_audit_is_append_only(EntityState state)
    {
        await using var database = CreateDatabase();
        var challenge = NewChallenge(Now);
        database.BuyerEmailChangeChallenges.Add(challenge);
        database.BuyerEmailChangeAuditEvents.Add(NewAudit(challenge));
        await database.SaveChangesAsync();
        database.Entry(
            database.BuyerEmailChangeAuditEvents.Single()).State = state;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.SaveChangesAsync());
    }

    private static BuyerEmailChangeChallenge ActiveChallenge(
        DateTimeOffset createdAt)
    {
        var challenge = NewChallenge(createdAt);
        challenge.MarkSendAccepted(createdAt.AddSeconds(1));
        return challenge;
    }

    private static BuyerEmailChangeChallenge NewChallenge(
        DateTimeOffset createdAt) =>
        BuyerEmailChangeChallenge.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "buyer@example.com",
            "b****@example.com",
            new string('a', 64),
            Guid.NewGuid().ToString("N"),
            createdAt);

    private static BuyerEmailChangeAuditEvent NewAudit(
        BuyerEmailChangeChallenge challenge) =>
        new(
            challenge.BuyerId,
            challenge.Id,
            "buyer.email_change.requested",
            new string('b', 64),
            challenge.MaskedPendingEmail,
            Now,
            "accepted");

    private static ToklongDbContext CreateDatabase()
    {
        var options =
            new DbContextOptionsBuilder<ToklongDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
        return new ToklongDbContext(options);
    }
}
