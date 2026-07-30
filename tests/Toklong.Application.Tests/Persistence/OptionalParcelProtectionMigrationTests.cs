using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Persistence.Migrations;

namespace Toklong.Application.Tests.Persistence;

public sealed class OptionalParcelProtectionMigrationTests
{
    private const string PostgreSqlConnectionStringEnvironmentVariable =
        "TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION";

    [RequiresPostgreSqlMigrationFixture]
    public async Task Up_backfills_persisted_legacy_rows_in_PostgreSql()
    {
        var adminConnectionString = Environment.GetEnvironmentVariable(
            PostgreSqlConnectionStringEnvironmentVariable)!;
        await using var database = await PostgreSqlMigrationDatabase.CreateAsync(
            adminConnectionString);
        await using var connection = new NpgsqlConnection(
            database.ConnectionString);
        await connection.OpenAsync();

        await CreatePreMigrationSchemaAsync(connection);

        var buyerAcceptedAt = new DateTimeOffset(
            2026,
            7,
            30,
            9,
            2,
            0,
            TimeSpan.Zero);
        var positiveLegacyRowId = Guid.Parse(
            "10000000-0000-0000-0000-000000000001");
        var zeroLegacyRowId = Guid.Parse(
            "10000000-0000-0000-0000-000000000002");

        await InsertLegacyRowsAsync(
            connection,
            positiveLegacyRowId,
            zeroLegacyRowId,
            buyerAcceptedAt);
        await ApplyUpMigrationAsync(connection);

        var rows = await ReadMigratedRowsAsync(connection);

        var positive = Assert.Single(
            rows,
            row => row.Id == positiveLegacyRowId);
        Assert.Equal("Accepted", positive.Election);
        Assert.Equal(4_500L, positive.ParcelInsuranceFeeSatang);
        Assert.Equal(4_500L, positive.ProviderCostSatang);
        Assert.Equal(0L, positive.ServiceFeeSatang);
        Assert.Equal("legacy-full-value-v1", positive.TermsVersion);
        Assert.Equal(buyerAcceptedAt, positive.BuyerElectedAt);
        Assert.Equal(0L, positive.IncludedCoverageSatang);
        Assert.Equal(0L, positive.SelectedCoverageSatang);

        var zero = Assert.Single(rows, row => row.Id == zeroLegacyRowId);
        Assert.Equal("Pending", zero.Election);
        Assert.Equal(0L, zero.ParcelInsuranceFeeSatang);
        Assert.Equal(0L, zero.ProviderCostSatang);
        Assert.Equal(0L, zero.ServiceFeeSatang);
        Assert.Null(zero.TermsVersion);
        Assert.Null(zero.BuyerElectedAt);
        Assert.Equal(0L, zero.IncludedCoverageSatang);
        Assert.Equal(0L, zero.SelectedCoverageSatang);
    }

    [Fact]
    public void Up_backfills_legacy_paid_protection_as_accepted()
    {
        var operations = MigrationProbe.CreateUpOperations();

        var backfill = Assert.Single(operations.OfType<SqlOperation>());

        Assert.Contains(
            "\"ParcelProtectionElection\" = 'Accepted'",
            backfill.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"ParcelProtectionProviderCostSatang\" = \"ParcelInsuranceFeeSatang\"",
            backfill.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"ParcelProtectionServiceFeeSatang\" = 0",
            backfill.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"ParcelProtectionTermsVersion\" = 'legacy-full-value-v1'",
            backfill.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "\"ParcelProtectionBuyerElectedAt\" = \"BuyerAcceptedAt\"",
            backfill.Sql,
            StringComparison.Ordinal);
        Assert.Contains(
            "WHERE \"ParcelInsuranceFeeSatang\" > 0",
            backfill.Sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ParcelProtectionIncludedCoverageSatang",
            backfill.Sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ParcelProtectionSelectedCoverageSatang",
            backfill.Sql,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Up_leaves_zero_fee_legacy_rows_pending_without_coverage_limits()
    {
        var operations = MigrationProbe.CreateUpOperations();

        var election = Assert.Single(
            operations.OfType<AddColumnOperation>(), operation =>
                operation.Table == "transactions" &&
                operation.Name == "ParcelProtectionElection");
        var includedCoverage = Assert.Single(
            operations.OfType<AddColumnOperation>(), operation =>
                operation.Table == "transactions" &&
                operation.Name == "ParcelProtectionIncludedCoverageSatang");
        var selectedCoverage = Assert.Single(
            operations.OfType<AddColumnOperation>(), operation =>
                operation.Table == "transactions" &&
                operation.Name == "ParcelProtectionSelectedCoverageSatang");

        Assert.Equal("Pending", election.DefaultValue);
        Assert.Equal(0L, includedCoverage.DefaultValue);
        Assert.Equal(0L, selectedCoverage.DefaultValue);
    }

    [Fact]
    public void Down_replaces_nullable_insurance_codes_before_restoring_not_null_column()
    {
        var operations = MigrationProbe.CreateDownOperations();

        var normalizationIndex = operations
            .Select((operation, index) => (operation, index))
            .Single(entry => entry.operation is SqlOperation sql &&
                sql.Sql.Contains(
                    "UPDATE managed_shipments",
                    StringComparison.Ordinal) &&
                sql.Sql.Contains(
                    "\"InsuranceCode\" = ''",
                    StringComparison.Ordinal) &&
                sql.Sql.Contains(
                    "\"InsuranceCode\" IS NULL",
                    StringComparison.Ordinal))
            .index;
        var restoreNotNullIndex = operations
            .Select((operation, index) => (operation, index))
            .Single(entry => entry.operation is AlterColumnOperation alter &&
                alter.Table == "managed_shipments" &&
                alter.Name == "InsuranceCode" &&
                !alter.IsNullable)
            .index;

        Assert.True(normalizationIndex < restoreNotNullIndex);
    }

    private sealed class MigrationProbe : OptionalParcelProtectionCheckout
    {
        public static IReadOnlyList<MigrationOperation> CreateUpOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            new MigrationProbe().Up(builder);
            return builder.Operations;
        }

        public static IReadOnlyList<MigrationOperation> CreateDownOperations()
        {
            var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
            new MigrationProbe().Down(builder);
            return builder.Operations;
        }
    }

    private static async Task CreatePreMigrationSchemaAsync(
        NpgsqlConnection connection)
    {
        await ExecuteNonQueryAsync(
            connection,
            """
            CREATE TABLE transactions (
                "Id" uuid PRIMARY KEY,
                "ParcelInsuranceFeeSatang" bigint NOT NULL,
                "BuyerAcceptedAt" timestamp with time zone NULL
            );

            CREATE TABLE managed_shipments (
                "Id" uuid PRIMARY KEY,
                "InsuranceCode" character varying(80) NOT NULL
            );
            """);
    }

    private static async Task InsertLegacyRowsAsync(
        NpgsqlConnection connection,
        Guid positiveLegacyRowId,
        Guid zeroLegacyRowId,
        DateTimeOffset buyerAcceptedAt)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO transactions (
                "Id",
                "ParcelInsuranceFeeSatang",
                "BuyerAcceptedAt")
            VALUES
                (@positiveLegacyRowId, 4500, @buyerAcceptedAt),
                (@zeroLegacyRowId, 0, @buyerAcceptedAt);
            """,
            connection);
        command.Parameters.AddWithValue(
            "positiveLegacyRowId",
            positiveLegacyRowId);
        command.Parameters.AddWithValue("zeroLegacyRowId", zeroLegacyRowId);
        command.Parameters.AddWithValue("buyerAcceptedAt", buyerAcceptedAt);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task ApplyUpMigrationAsync(NpgsqlConnection connection)
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseNpgsql(connection.ConnectionString)
            .Options;
        await using var context = new ToklongDbContext(options);
        var generator = context.GetService<IMigrationsSqlGenerator>();
        var commands = generator.Generate(
            MigrationProbe.CreateUpOperations(),
            context.Model);

        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var migrationCommand in commands)
        {
            await using var command = new NpgsqlCommand(
                migrationCommand.CommandText,
                connection,
                transaction);
            await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();
    }

    private static async Task<IReadOnlyList<LegacyMigrationRow>>
        ReadMigratedRowsAsync(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                "Id",
                "ParcelProtectionElection",
                "ParcelInsuranceFeeSatang",
                "ParcelProtectionProviderCostSatang",
                "ParcelProtectionServiceFeeSatang",
                "ParcelProtectionTermsVersion",
                "ParcelProtectionBuyerElectedAt",
                "ParcelProtectionIncludedCoverageSatang",
                "ParcelProtectionSelectedCoverageSatang"
            FROM transactions
            ORDER BY "Id";
            """,
            connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<LegacyMigrationRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(
                new LegacyMigrationRow(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    reader.GetInt64(3),
                    reader.GetInt64(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6)
                        ? null
                        : reader.GetFieldValue<DateTimeOffset>(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8)));
        }

        return rows;
    }

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        string commandText)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed record LegacyMigrationRow(
        Guid Id,
        string Election,
        long ParcelInsuranceFeeSatang,
        long ProviderCostSatang,
        long ServiceFeeSatang,
        string? TermsVersion,
        DateTimeOffset? BuyerElectedAt,
        long IncludedCoverageSatang,
        long SelectedCoverageSatang);

    private sealed class PostgreSqlMigrationDatabase : IAsyncDisposable
    {
        private readonly string adminConnectionString;
        private readonly string databaseName;

        private PostgreSqlMigrationDatabase(
            string adminConnectionString,
            string databaseName,
            string connectionString)
        {
            this.adminConnectionString = adminConnectionString;
            this.databaseName = databaseName;
            ConnectionString = connectionString;
        }

        public string ConnectionString { get; }

        public static async Task<PostgreSqlMigrationDatabase> CreateAsync(
            string adminConnectionString)
        {
            var builder = new NpgsqlConnectionStringBuilder(adminConnectionString);
            var databaseName = $"toklong_migration_{Guid.NewGuid():N}";
            await using var admin = new NpgsqlConnection(builder.ConnectionString);
            await admin.OpenAsync();
            await ExecuteDatabaseCommandAsync(
                admin,
                $"CREATE DATABASE \"{databaseName}\";");

            builder.Database = databaseName;
            return new PostgreSqlMigrationDatabase(
                adminConnectionString,
                databaseName,
                builder.ConnectionString);
        }

        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(adminConnectionString);
            await admin.OpenAsync();
            await ExecuteDatabaseCommandAsync(
                admin,
                $"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE);");
        }

        private static async Task ExecuteDatabaseCommandAsync(
            NpgsqlConnection connection,
            string commandText)
        {
            await using var command = new NpgsqlCommand(commandText, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class RequiresPostgreSqlMigrationFixtureAttribute : FactAttribute
{
    public RequiresPostgreSqlMigrationFixtureAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(
                "TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION")))
        {
            Skip =
                "Set TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION to a PostgreSQL " +
                "connection string for a role permitted to CREATE/DROP DATABASE.";
        }
    }
}
