using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Toklong.Domain.Accounts;
using Toklong.Domain.Authentication;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;

namespace Toklong.Application.Tests.Persistence;

public sealed class AccountNameChangePostgreSqlTests
{
    private const string ConnectionEnvironmentVariable =
        "TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION";
    private const string PreviousMigration =
        "20260731090000_StructuredAccountNames";
    private static readonly DateTimeOffset Now =
        new(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);

    [RequiresPostgreSqlMigrationFixture]
    public async Task Migration_enforces_partial_uniqueness_foreign_keys_and_verification_provenance()
    {
        await using var database =
            await PostgreSqlDatabase.CreateAsync(
                Environment.GetEnvironmentVariable(
                    ConnectionEnvironmentVariable)!);
        await using var setup = database.CreateContext();
        await setup.Database.MigrateAsync();
        var party = await SeedPartyAndActiveChallengeAsync(setup);

        await using (var duplicateOpen = database.CreateContext())
        {
            var challenge = NewChallenge(
                party,
                "+66812345678");
            challenge.MarkSendAccepted(
                "provider-duplicate-open",
                Now.AddSeconds(1));
            duplicateOpen.AccountNameChangeChallenges.Add(
                challenge);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => duplicateOpen.SaveChangesAsync());
        }

        await using (var invalidForeignKey = database.CreateContext())
        {
            var challenge = AccountNameChangeChallenge.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                null,
                party.SessionId,
                "+66899999999",
                "089-•••-9999",
                AccountName.Create("ทดสอบ", "ต่างบัญชี"),
                Guid.NewGuid().ToString("N"),
                Now);
            invalidForeignKey.AccountNameChangeChallenges.Add(
                challenge);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => invalidForeignKey.SaveChangesAsync());
        }

        var verificationKey = Guid.NewGuid().ToString("N");
        await using (var firstAttempt = database.CreateContext())
        {
            firstAttempt.AccountNameVerificationAttempts.Add(
                NewAttempt(
                    party,
                    verificationKey));
            await firstAttempt.SaveChangesAsync();
        }

        await using (var duplicateAttempt = database.CreateContext())
        {
            duplicateAttempt.AccountNameVerificationAttempts.Add(
                NewAttempt(
                    party,
                    verificationKey));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => duplicateAttempt.SaveChangesAsync());
        }

        await using (var restrictiveDelete = database.CreateContext())
        {
            var buyer = await restrictiveDelete.Buyers
                .SingleAsync(value => value.Id == party.BuyerId);
            restrictiveDelete.Buyers.Remove(buyer);

            await Assert.ThrowsAsync<DbUpdateException>(
                () => restrictiveDelete.SaveChangesAsync());
        }

        await using (var firstReplacement = database.CreateContext())
        {
            var source = await firstReplacement
                .AccountNameChangeChallenges
                .SingleAsync(value =>
                    value.Id == party.ChallengeId);
            source.Supersede(Now.AddMinutes(1));
            firstReplacement.AccountNameChangeChallenges.Add(
                NewResend(
                    party,
                    "+66822222222",
                    Guid.NewGuid().ToString("N")));
            await firstReplacement.SaveChangesAsync();
        }

        await using (var secondReplacement = database.CreateContext())
        {
            secondReplacement.AccountNameChangeChallenges.Add(
                NewResend(
                    party,
                    "+66833333333",
                    Guid.NewGuid().ToString("N")));

            await Assert.ThrowsAsync<DbUpdateException>(
                () => secondReplacement.SaveChangesAsync());
        }
    }

    [RequiresPostgreSqlMigrationFixture]
    public async Task Version_token_rejects_a_second_context_update()
    {
        await using var database =
            await PostgreSqlDatabase.CreateAsync(
                Environment.GetEnvironmentVariable(
                    ConnectionEnvironmentVariable)!);
        await using var setup = database.CreateContext();
        await setup.Database.MigrateAsync();
        var party = await SeedPartyAndActiveChallengeAsync(setup);

        await using var first = database.CreateContext();
        await using var second = database.CreateContext();
        var firstCopy = await first.AccountNameChangeChallenges
            .SingleAsync(value => value.Id == party.ChallengeId);
        var secondCopy = await second.AccountNameChangeChallenges
            .SingleAsync(value => value.Id == party.ChallengeId);
        firstCopy.Supersede(Now.AddMinutes(1));
        secondCopy.Supersede(Now.AddMinutes(2));

        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => second.SaveChangesAsync());
    }

    [RequiresPostgreSqlMigrationFixture]
    public async Task Migration_up_down_and_up_restores_all_name_change_tables()
    {
        await using var database =
            await PostgreSqlDatabase.CreateAsync(
                Environment.GetEnvironmentVariable(
                    ConnectionEnvironmentVariable)!);
        await using var context = database.CreateContext();
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync();
        Assert.Equal(3, await CountNameChangeTablesAsync(
            database.ConnectionString));

        await migrator.MigrateAsync(PreviousMigration);
        Assert.Equal(0, await CountNameChangeTablesAsync(
            database.ConnectionString));

        await migrator.MigrateAsync();
        Assert.Equal(3, await CountNameChangeTablesAsync(
            database.ConnectionString));
    }

    private static async Task<Party> SeedPartyAndActiveChallengeAsync(
        ToklongDbContext context)
    {
        var name = AccountName.Create("สมชาย", "ใจดี");
        var buyer = BuyerAccount.Create(
            "+66812345678",
            name,
            "account-name-pg@example.test",
            Now);
        var seller = SellerAccount.Create(
            "+66812345678",
            Now,
            name);
        var session = MobileSession.Create(
            buyer.Id,
            seller.Id,
            name.DisplayName,
            "+66812345678",
            new string('a', 64),
            Now,
            Now.AddDays(1));
        var party = new Party(
            buyer.Id,
            seller.Id,
            session.Id,
            Guid.NewGuid());
        var challenge = AccountNameChangeChallenge.Create(
            party.ChallengeId,
            party.BuyerId,
            party.SellerId,
            party.SessionId,
            "+66812345678",
            "081-•••-5678",
            AccountName.Create("สมศักดิ์", "ใจดี"),
            Guid.NewGuid().ToString("N"),
            Now);
        challenge.MarkSendAccepted(
            "provider-initial",
            Now);

        context.AddRange(
            buyer,
            seller,
            session,
            challenge);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        return party;
    }

    private static AccountNameChangeChallenge NewChallenge(
        Party party,
        string phoneNumber) =>
        AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            party.BuyerId,
            party.SellerId,
            party.SessionId,
            phoneNumber,
            "081-•••-5678",
            AccountName.Create("สมศักดิ์", "ใจดี"),
            Guid.NewGuid().ToString("N"),
            Now);

    private static AccountNameVerificationAttempt NewAttempt(
        Party party,
        string key) =>
        new(
            Guid.NewGuid(),
            party.BuyerId,
            party.SellerId,
            party.SessionId,
            party.ChallengeId,
            key,
            new string('b', 64),
            AccountNameVerificationAttemptOutcome.Incorrect,
            4,
            Now,
            null);

    private static AccountNameChangeChallenge NewResend(
        Party party,
        string phoneNumber,
        string requestKey) =>
        AccountNameChangeChallenge.Create(
            Guid.NewGuid(),
            party.BuyerId,
            party.SellerId,
            party.SessionId,
            phoneNumber,
            "081-•••-5678",
            AccountName.Create("สมศักดิ์", "ใจดี"),
            requestKey,
            Now.AddMinutes(1),
            party.ChallengeId);

    private static async Task<int> CountNameChangeTablesAsync(
        string connectionString)
    {
        await using var connection =
            new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT COUNT(*)::integer
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_name IN (
                'account_name_change_challenges',
                'account_name_change_audit_events',
                'account_name_verification_attempts');
            """,
            connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }

    private sealed record Party(
        Guid BuyerId,
        Guid SellerId,
        Guid SessionId,
        Guid ChallengeId);

    private sealed class PostgreSqlDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private PostgreSqlDatabase(
            string adminConnectionString,
            string databaseName,
            string connectionString)
        {
            this.adminConnectionString = adminConnectionString;
            this.databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<PostgreSqlDatabase> CreateAsync(
            string adminConnectionString)
        {
            var builder =
                new NpgsqlConnectionStringBuilder(
                    adminConnectionString);
            var databaseName =
                $"toklong_account_name_{Guid.NewGuid():N}";
            await using var admin =
                new NpgsqlConnection(builder.ConnectionString);
            await admin.OpenAsync();
            await ExecuteAsync(
                admin,
                $"CREATE DATABASE \"{databaseName}\";");
            builder.Database = databaseName;
            return new PostgreSqlDatabase(
                adminConnectionString,
                databaseName,
                builder.ConnectionString);
        }

        public ToklongDbContext CreateContext()
        {
            var options =
                new DbContextOptionsBuilder<ToklongDbContext>()
                    .UseNpgsql(ConnectionString)
                    .Options;
            return new ToklongDbContext(options);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin =
                new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await ExecuteAsync(
                admin,
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
        }

        private static async Task ExecuteAsync(
            NpgsqlConnection connection,
            string commandText)
        {
            await using var command =
                new NpgsqlCommand(commandText, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
