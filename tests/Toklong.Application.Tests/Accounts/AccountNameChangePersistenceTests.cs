using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Toklong.Application.Abstractions;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Common;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Email;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Persistence.Migrations;
using Toklong.Infrastructure.Security;

namespace Toklong.Application.Tests.Accounts;

public sealed class AccountNameChangePersistenceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Model_allows_only_one_pending_or_active_challenge_per_phone()
    {
        using var database = CreateDatabase();
        var entity = database.Model.FindEntityType(
            typeof(AccountNameChangeChallenge))!;
        var index = Assert.Single(
            entity.GetIndexes(),
            value => value.IsUnique &&
                     value.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(AccountNameChangeChallenge.PhoneNumber)
                         ]));

        Assert.Contains("PendingSend", index.GetFilter());
        Assert.Contains("Active", index.GetFilter());
    }

    [Fact]
    public void Model_has_bounded_provider_reference_concurrency_and_lookup_indexes()
    {
        using var database = CreateDatabase();
        var challenge = database.Model.FindEntityType(
            typeof(AccountNameChangeChallenge))!;
        var attempt = database.Model.FindEntityType(
            typeof(AccountNameVerificationAttempt))!;
        var audit = database.Model.FindEntityType(
            typeof(AccountNameChangeAuditEvent))!;

        Assert.Equal(
            800,
            challenge.FindProperty(
                nameof(AccountNameChangeChallenge.ProviderChallengeId))!
                .GetMaxLength());
        Assert.Equal(
            64,
            challenge.FindProperty(
                nameof(AccountNameChangeChallenge.SendFailureCode))!
                .GetMaxLength());
        Assert.Equal(
            200,
            challenge.FindProperty(
                nameof(AccountNameChangeChallenge.SendFailureMessage))!
                .GetMaxLength());
        Assert.Equal(
            32,
            challenge.FindProperty(
                nameof(AccountNameChangeChallenge.RequestIdempotencyKey))!
                .GetMaxLength());
        Assert.Equal(
            32,
            challenge.FindProperty(
                nameof(AccountNameChangeChallenge.ProviderRequestKey))!
                .GetMaxLength());
        Assert.Equal(
            64,
            challenge.FindProperty(
                nameof(AccountNameChangeChallenge.OperationFingerprint))!
                .GetMaxLength());
        Assert.True(
            challenge.FindProperty(
                nameof(AccountNameChangeChallenge.Version))!
                .IsConcurrencyToken);
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(AccountNameChangeChallenge.PhoneNumber),
                             nameof(AccountNameChangeChallenge.RequestIdempotencyKey)
                         ]));
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(AccountNameChangeChallenge.ProviderRequestKey)
                         ]));
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(AccountNameChangeChallenge.SourceChallengeId)
                         ]) &&
                     index.GetFilter() ==
                         "\"SourceChallengeId\" IS NOT NULL");
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(AccountNameChangeChallenge.BuyerId),
                    nameof(AccountNameChangeChallenge.SendAcceptedAt)
                ]));
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(AccountNameChangeChallenge.SellerId),
                    nameof(AccountNameChangeChallenge.SendAcceptedAt)
                ]));
        Assert.Contains(
            challenge.GetIndexes(),
            index => index.Properties.Select(property => property.Name)
                .SequenceEqual([
                    nameof(AccountNameChangeChallenge.PhoneNumber),
                    nameof(AccountNameChangeChallenge.SendAcceptedAt)
                ]));

        Assert.Equal(
            64,
            attempt.FindProperty(
                nameof(AccountNameVerificationAttempt.SubmittedDigest))!
                .GetMaxLength());
        Assert.Contains(
            attempt.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(AccountNameVerificationAttempt.ChallengeId),
                             nameof(AccountNameVerificationAttempt.IdempotencyKey)
                         ]));
        Assert.Equal(
            120,
            audit.FindProperty(
                nameof(AccountNameChangeAuditEvent.OldName))!.GetMaxLength());
        Assert.Equal(
            120,
            audit.FindProperty(
                nameof(AccountNameChangeAuditEvent.NewName))!.GetMaxLength());
    }

    [Fact]
    public void Model_uses_restrictive_foreign_keys_for_account_security_evidence()
    {
        using var database = CreateDatabase();

        foreach (var type in new[]
                 {
                     typeof(AccountNameChangeChallenge),
                     typeof(AccountNameVerificationAttempt),
                     typeof(AccountNameChangeAuditEvent)
                 })
        {
            var foreignKeys = database.Model.FindEntityType(type)!
                .GetForeignKeys()
                .ToArray();
            Assert.NotEmpty(foreignKeys);
            Assert.All(
                foreignKeys,
                foreignKey =>
                    Assert.Equal(
                        DeleteBehavior.Restrict,
                        foreignKey.DeleteBehavior));
        }
    }

    [Fact]
    public void Migration_contains_provider_and_resend_provenance_constraints()
    {
        var operations = MigrationProbe.CreateUpOperations();
        var table = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation =>
                operation.Name == "account_name_change_challenges");

        Assert.Contains(
            table.Columns,
            column => column.Name == "ProviderRequestKey" &&
                      column.MaxLength == 32 &&
                      !column.IsNullable);
        Assert.Contains(
            table.Columns,
            column => column.Name == "OperationKind" &&
                      column.MaxLength == 16 &&
                      !column.IsNullable);
        Assert.Contains(
            table.Columns,
            column => column.Name == "SourceChallengeId" &&
                      column.IsNullable);
        Assert.Contains(
            table.Columns,
            column => column.Name == "OperationFingerprint" &&
                      column.MaxLength == 64 &&
                      !column.IsNullable);
        Assert.Contains(
            table.Columns,
            column => column.Name == "SendFailureCode" &&
                      column.MaxLength == 64 &&
                      column.IsNullable);
        Assert.Contains(
            table.Columns,
            column => column.Name == "SendFailureMessage" &&
                      column.MaxLength == 200 &&
                      column.IsNullable);
        Assert.Contains(
            table.Columns,
            column =>
                column.Name == "SendFailureRetryAfterTicks" &&
                column.IsNullable);
        Assert.Contains(
            operations.OfType<CreateIndexOperation>(),
            index => index.IsUnique &&
                     index.Table ==
                         "account_name_change_challenges" &&
                     index.Columns.SequenceEqual([
                         "ProviderRequestKey"
                     ]));
        Assert.Contains(
            operations.OfType<CreateIndexOperation>(),
            index => index.IsUnique &&
                     index.Table ==
                         "account_name_change_challenges" &&
                     index.Columns.SequenceEqual([
                         "SourceChallengeId"
                     ]) &&
                     index.Filter ==
                         "\"SourceChallengeId\" IS NOT NULL");
        Assert.Contains(
            table.ForeignKeys,
            foreignKey =>
                foreignKey.Columns.SequenceEqual([
                    "SourceChallengeId"
                ]) &&
                foreignKey.PrincipalTable ==
                    "account_name_change_challenges" &&
                foreignKey.OnDelete ==
                    ReferentialAction.Restrict);

        Assert.Contains(
            MigrationProbe.CreateDownOperations()
                .OfType<DropTableOperation>(),
            operation =>
                operation.Name ==
                    "account_name_change_challenges");
    }

    [Fact]
    public async Task Repository_round_trips_supported_challenge_lookups()
    {
        await using var database = CreateDatabase();
        var repository = new AccountNameChangeRepository(database);
        var challenge = NewChallenge();
        challenge.MarkSendAccepted("provider-reference", Now);
        await repository.AddAsync(challenge, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var byId = await repository.GetByIdAsync(challenge.Id, default);
        var open = await repository.GetOpenAsync(
            challenge.PhoneNumber,
            default);
        var byRequest = await repository.GetByRequestKeyAsync(
            challenge.PhoneNumber,
            challenge.RequestIdempotencyKey,
            default);

        Assert.Equal(challenge.Id, byId?.Id);
        Assert.Equal(challenge.Id, open?.Id);
        Assert.Equal(challenge.Id, byRequest?.Id);
        Assert.Equal("provider-reference", byId?.ProviderChallengeId);
        Assert.Equal(
            challenge.ProviderRequestKey,
            byId?.ProviderRequestKey);
        Assert.Equal(
            challenge.OperationFingerprint,
            byId?.OperationFingerprint);
    }

    [Fact]
    public async Task Repository_round_trips_bounded_send_rejection_evidence()
    {
        await using var database = CreateDatabase();
        var repository = new AccountNameChangeRepository(database);
        var challenge = NewChallenge();
        challenge.MarkSendFailed(
            Now,
            "otp_provider_cooldown",
            "กรุณารออีก 37 วินาที",
            TimeSpan.FromSeconds(37.25));
        await repository.AddAsync(challenge, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var stored = await repository.GetByIdAsync(
            challenge.Id,
            default);

        Assert.Equal(
            "otp_provider_cooldown",
            stored?.SendFailureCode);
        Assert.Equal(
            "กรุณารออีก 37 วินาที",
            stored?.SendFailureMessage);
        Assert.Equal(
            TimeSpan.FromSeconds(37.25).Ticks,
            stored?.SendFailureRetryAfterTicks);
    }

    [Fact]
    public async Task Repository_distinguishes_exact_resend_replay_from_source_conflict()
    {
        await using var database = CreateDatabase();
        var repository = new AccountNameChangeRepository(database);
        var source = NewChallenge();
        source.MarkSendAccepted("provider-source", Now);
        source.Supersede(Now.AddMinutes(2));
        var key = Guid.NewGuid().ToString("N");
        var resend = AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            source.BuyerId,
            source.SellerId,
            source.SessionId,
            source.PhoneNumber,
            source.MaskedPhoneNumber,
            AccountName.Create(
                source.PendingFirstName,
                source.PendingLastName),
            key,
            Now.AddMinutes(2),
            source.Id);
        await repository.AddAsync(source, default);
        await repository.AddAsync(resend, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var replay = await repository.GetByRequestKeyAsync(
            resend.PhoneNumber,
            key,
            default);
        var bySource =
            await repository.GetBySourceChallengeIdAsync(
                source.Id,
                default);

        Assert.Equal(source.Id, replay?.SourceChallengeId);
        Assert.Equal(resend.Id, bySource?.Id);
        Assert.Equal(
            resend.OperationFingerprint,
            replay?.OperationFingerprint);
        var pendingName = AccountName.Create(
            resend.PendingFirstName,
            resend.PendingLastName);
        replay!.EnsureExactOperationReplay(
            key,
            source.Id,
            pendingName);
        Assert.Throws<DomainException>(() =>
            replay.EnsureExactOperationReplay(
                key,
                Guid.NewGuid(),
                pendingName));
        Assert.Throws<DomainException>(() =>
            replay.EnsureExactOperationReplay(
                Guid.NewGuid().ToString("N"),
                source.Id,
                pendingName));
    }

    [Fact]
    public async Task Repository_counts_only_accepted_sends_in_rolling_window_for_subject()
    {
        await using var database = CreateDatabase();
        var repository = new AccountNameChangeRepository(database);
        var first = NewChallenge();
        first.MarkSendAccepted("provider-one", Now.AddHours(-23));
        first.Supersede(Now.AddHours(-22));
        var second = NewChallenge();
        second.MarkSendAccepted("provider-two", Now.AddHours(-1));
        var old = NewChallenge();
        old.MarkSendAccepted("provider-old", Now.AddHours(-25));
        old.Supersede(Now.AddHours(-24));
        var failed = NewChallenge();
        failed.MarkSendFailed(Now.AddMinutes(-2));
        foreach (var value in new[] { first, second, old, failed })
            await repository.AddAsync(value, default);
        await database.SaveChangesAsync();

        var count = await repository.CountAcceptedSendsAsync(
            second.BuyerId,
            second.SellerId,
            second.PhoneNumber,
            Now.AddHours(-24),
            default);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task Repository_adds_attempt_and_audit_without_persisting_raw_code()
    {
        await using var database = CreateDatabase();
        var repository = new AccountNameChangeRepository(database);
        var challenge = NewChallenge();
        var digest = new string('a', 64);
        var attempt = NewAttempt(challenge, digest);
        var audit = NewAudit(challenge);
        await repository.AddAsync(challenge, default);
        await repository.AddAttemptAsync(attempt, default);
        await repository.AddAuditAsync(audit, default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var storedAttempt = await repository.GetAttemptAsync(
            attempt.ChallengeId,
            attempt.IdempotencyKey,
            default);
        var storedAudit =
            Assert.Single(database.AccountNameChangeAuditEvents);
        Assert.Equal(digest, storedAttempt?.SubmittedDigest);
        Assert.DoesNotContain("123456", storedAttempt?.SubmittedDigest);
        Assert.Equal(challenge.Id, storedAudit.ChallengeId);
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Account_name_audit_is_append_only(EntityState state)
    {
        await using var database = CreateDatabase();
        var challenge = NewChallenge();
        database.AccountNameChangeChallenges.Add(challenge);
        database.AccountNameChangeAuditEvents.Add(NewAudit(challenge));
        await database.SaveChangesAsync();
        database.Entry(
            database.AccountNameChangeAuditEvents.Single()).State = state;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.SaveChangesAsync());
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public async Task Account_name_verification_attempt_is_append_only(
        EntityState state)
    {
        await using var database = CreateDatabase();
        var challenge = NewChallenge();
        database.AccountNameChangeChallenges.Add(challenge);
        database.AccountNameVerificationAttempts.Add(
            NewAttempt(challenge, new string('b', 64)));
        await database.SaveChangesAsync();
        database.Entry(
            database.AccountNameVerificationAttempts.Single()).State = state;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => database.SaveChangesAsync());
    }

    [Fact]
    public void Security_digest_uses_the_account_name_domain_and_never_returns_code()
    {
        var key = "account-name-test-secret-that-is-long-enough";
        var challengeId =
            Guid.Parse("11111111-2222-3333-4444-555555555555");
        var security = new AccountNameVerificationSecurity(
            new EmailVerificationOptions { DigestKey = key });

        var digest = security.Digest(challengeId, "123456");
        var expected = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(key),
                    Encoding.UTF8.GetBytes(
                        "account-name:11111111222233334444555555555555:123456")))
            .ToLowerInvariant();

        Assert.Equal(expected, digest);
        Assert.Equal(64, digest.Length);
        Assert.DoesNotContain("123456", digest);
        Assert.NotEqual(
            digest,
            security.Digest(Guid.NewGuid(), "123456"));
        Assert.Throws<ArgumentException>(
            () => security.Digest(challengeId, " 123456 "));

        var auditReference = security.DigestAuditValue(
            challengeId,
            "สมชาย ใจดี");
        var expectedAuditReference = Convert.ToHexString(
                HMACSHA256.HashData(
                    Encoding.UTF8.GetBytes(key),
                    Encoding.UTF8.GetBytes(
                        "account-name-audit:" +
                        "11111111222233334444555555555555:" +
                        "สมชาย ใจดี")))
            .ToLowerInvariant();
        Assert.Equal(expectedAuditReference, auditReference);
        Assert.DoesNotContain("สมชาย", auditReference);
        Assert.NotEqual(digest, auditReference);
    }

    private static AccountNameChangeChallenge NewChallenge()
    {
        var party = PartyIds.Value;
        return AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            party.BuyerId,
            party.SellerId,
            party.SessionId,
            "+66812345678",
            "081-•••-5678",
            AccountName.Create("สมศักดิ์", "ใจดี"),
            Guid.NewGuid().ToString("N"),
            Now);
    }

    private static AccountNameVerificationAttempt NewAttempt(
        AccountNameChangeChallenge challenge,
        string digest) =>
        new(
            Guid.NewGuid(),
            challenge.BuyerId,
            challenge.SellerId,
            challenge.SessionId,
            challenge.Id,
            Guid.NewGuid().ToString("N"),
            digest,
            AccountNameVerificationAttemptOutcome.Incorrect,
            4,
            Now,
            null);

    private static AccountNameChangeAuditEvent NewAudit(
        AccountNameChangeChallenge challenge) =>
        new(
            challenge.BuyerId,
            challenge.SellerId,
            challenge.SessionId,
            challenge.Id,
            new string('a', 64),
            new string('b', 64),
            Now,
            "account.name_change.verified",
            "verified");

    private static readonly Lazy<(
        Guid BuyerId,
        Guid SellerId,
        Guid SessionId)> PartyIds = new(() =>
        (
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000002"),
            Guid.Parse("30000000-0000-0000-0000-000000000003")
        ));

    private static ToklongDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ToklongDbContext(options);
    }

    private sealed class MigrationProbe : VerifiedAccountNameChange
    {
        public static IReadOnlyList<MigrationOperation>
            CreateUpOperations()
        {
            var builder = new MigrationBuilder(
                "Npgsql.EntityFrameworkCore.PostgreSQL");
            new MigrationProbe().Up(builder);
            return builder.Operations;
        }

        public static IReadOnlyList<MigrationOperation>
            CreateDownOperations()
        {
            var builder = new MigrationBuilder(
                "Npgsql.EntityFrameworkCore.PostgreSQL");
            new MigrationProbe().Down(builder);
            return builder.Operations;
        }
    }
}
