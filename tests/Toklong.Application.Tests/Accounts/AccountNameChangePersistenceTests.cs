using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
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
        var operation = database.Model.FindEntityType(
            typeof(AccountNameVerificationOperation))!;
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
        Assert.True(
            operation.FindProperty(
                nameof(AccountNameVerificationOperation.Version))!
                .IsConcurrencyToken);
        Assert.Equal(
            32,
            operation.FindProperty(
                nameof(AccountNameVerificationOperation.ProviderVerificationKey))!
                .GetMaxLength());
        Assert.Contains(
            operation.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(AccountNameVerificationOperation.ChallengeId),
                             nameof(AccountNameVerificationOperation.IdempotencyKey)
                         ]));
        Assert.Contains(
            operation.GetIndexes(),
            index => index.IsUnique &&
                     index.Properties.Select(property => property.Name)
                         .SequenceEqual([
                             nameof(AccountNameVerificationOperation.ProviderVerificationKey)
                         ]));
        Assert.Equal(
            "bytea",
            audit.FindProperty(
                nameof(AccountNameChangeAuditEvent.ProtectedNameEvidence))!
                .FindAnnotation(
                    "Relational:ColumnType")!
                .Value);
        Assert.Equal(
            32,
            audit.FindProperty(
                nameof(AccountNameChangeAuditEvent.ProtectionVersion))!
                .GetMaxLength());
        Assert.Equal(
            64,
            audit.FindProperty(
                nameof(AccountNameChangeAuditEvent.LegacyOldNameDigest))!
                .GetMaxLength());
        Assert.Equal(
            64,
            audit.FindProperty(
                nameof(AccountNameChangeAuditEvent.LegacyNewNameDigest))!
                .GetMaxLength());
    }

    [Fact]
    public void Model_uses_restrictive_foreign_keys_for_account_security_evidence()
    {
        using var database = CreateDatabase();

        foreach (var type in new[]
                 {
                     typeof(AccountNameChangeChallenge),
                     typeof(AccountNameVerificationAttempt),
                     typeof(AccountNameVerificationOperation),
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
    public void Hardening_migration_preserves_legacy_audit_digests_and_adds_durable_operation()
    {
        var operations = HardeningMigrationProbe.CreateUpOperations();

        Assert.Contains(
            operations.OfType<RenameColumnOperation>(),
            operation =>
                operation.Table == "account_name_change_audit_events" &&
                operation.Name == "OldName" &&
                operation.NewName == "LegacyOldNameDigest");
        Assert.Contains(
            operations.OfType<RenameColumnOperation>(),
            operation =>
                operation.Table == "account_name_change_audit_events" &&
                operation.Name == "NewName" &&
                operation.NewName == "LegacyNewNameDigest");
        Assert.DoesNotContain(
            operations.OfType<DropColumnOperation>(),
            operation =>
                operation.Table == "account_name_change_audit_events" &&
                operation.Name is "OldName" or "NewName");

        var protectionVersion = Assert.Single(
            operations.OfType<AddColumnOperation>(),
            operation =>
                operation.Table == "account_name_change_audit_events" &&
                operation.Name == "ProtectionVersion");
        Assert.Equal("legacy-hmac:v1", protectionVersion.DefaultValue);
        Assert.Contains(
            operations.OfType<AddColumnOperation>(),
            operation =>
                operation.Table == "account_name_change_audit_events" &&
                operation.Name == "ProtectedNameEvidence" &&
                operation.ColumnType == "bytea" &&
                operation.IsNullable);

        var table = Assert.Single(
            operations.OfType<CreateTableOperation>(),
            operation =>
                operation.Name ==
                "account_name_verification_operations");
        Assert.Contains(
            table.Columns,
            column =>
                column.Name == "ProviderVerificationKey" &&
                column.MaxLength == 32 &&
                !column.IsNullable);
        Assert.Contains(
            table.ForeignKeys,
            foreignKey =>
                foreignKey.Columns.SequenceEqual(["ChallengeId"]) &&
                foreignKey.PrincipalTable ==
                "account_name_change_challenges" &&
                foreignKey.OnDelete == ReferentialAction.Restrict);
        Assert.Contains(
            operations.OfType<CreateIndexOperation>(),
            index =>
                index.Table ==
                "account_name_verification_operations" &&
                index.IsUnique &&
                index.Columns.SequenceEqual([
                    "ProviderVerificationKey"
                ]));

        var down = HardeningMigrationProbe.CreateDownOperations();
        Assert.Contains(
            down.OfType<RenameColumnOperation>(),
            operation =>
                operation.Name == "LegacyOldNameDigest" &&
                operation.NewName == "OldName");
        Assert.Contains(
            down.OfType<DropTableOperation>(),
            operation =>
                operation.Name ==
                "account_name_verification_operations");
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

    [Fact]
    public async Task Repository_round_trips_durable_provider_verification_operation()
    {
        await using var database = CreateDatabase();
        var repository = new AccountNameChangeRepository(database);
        var challenge = NewChallenge();
        challenge.MarkSendAccepted("provider-reference", Now);
        var key = Guid.NewGuid().ToString("N");
        var operation = new AccountNameVerificationOperation(
            Guid.NewGuid(),
            challenge.Id,
            key,
            new string('c', 64),
            Guid.NewGuid().ToString("N"),
            challenge.PhoneNumber,
            challenge.ProviderChallengeId!,
            Now);
        operation.RecordProviderOutcome(
            true,
            Now,
            Now.AddSeconds(1),
            Now.AddSeconds(1));
        await repository.AddAsync(challenge, default);
        await repository.AddVerificationOperationAsync(
            operation,
            default);
        await database.SaveChangesAsync();
        database.ChangeTracker.Clear();

        var stored =
            await repository.GetVerificationOperationAsync(
                challenge.Id,
                key,
                default);

        Assert.Equal(
            AccountNameVerificationOperationStatus.ProviderVerified,
            stored?.Status);
        Assert.Equal(
            operation.ProviderVerificationKey,
            stored?.ProviderVerificationKey);
        Assert.Equal(Now.AddSeconds(1), stored?.ProviderCompletedAt);
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
    public void Audit_name_evidence_round_trips_without_plaintext_at_rest()
    {
        var protector = new AccountNameAuditEvidenceProtector(
            new EphemeralDataProtectionProvider());
        var names = new AccountNameAuditEvidence(
            "สมชาย ใจดี",
            "สมชาย ใจเย็น",
            "สมศักดิ์ ใจดี");

        var protectedEvidence = protector.Protect(names);
        var recovered = protector.UnprotectForAuditReview(
            protectedEvidence);

        Assert.Equal("aspnet-dp:v1", protectedEvidence.ProtectionVersion);
        Assert.Equal(names, recovered);
        Assert.True(
            protectedEvidence.Ciphertext.AsSpan().IndexOf(
                Encoding.UTF8.GetBytes("สมชาย ใจดี")) < 0);
        Assert.True(
            protectedEvidence.Ciphertext.AsSpan().IndexOf(
                Encoding.UTF8.GetBytes("สมศักดิ์ ใจดี")) < 0);
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
            Enumerable.Repeat((byte)0xA5, 32).ToArray(),
            "test:v1",
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

    private sealed class HardeningMigrationProbe
        : HardenAccountNameVerification
    {
        public static IReadOnlyList<MigrationOperation>
            CreateUpOperations()
        {
            var builder = new MigrationBuilder(
                "Npgsql.EntityFrameworkCore.PostgreSQL");
            new HardeningMigrationProbe().Up(builder);
            return builder.Operations;
        }

        public static IReadOnlyList<MigrationOperation>
            CreateDownOperations()
        {
            var builder = new MigrationBuilder(
                "Npgsql.EntityFrameworkCore.PostgreSQL");
            new HardeningMigrationProbe().Down(builder);
            return builder.Operations;
        }
    }
}
