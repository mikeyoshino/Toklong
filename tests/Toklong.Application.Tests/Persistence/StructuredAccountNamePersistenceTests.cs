using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using Toklong.Domain.Buyers;
using Toklong.Domain.Sellers;
using Toklong.Infrastructure.Persistence;
using Toklong.Infrastructure.Persistence.Migrations;

namespace Toklong.Application.Tests.Persistence;

public sealed class StructuredAccountNamePersistenceTests
{
    private const string PostgreSqlConnectionStringEnvironmentVariable =
        "TOKLONG_POSTGRES_MIGRATION_TEST_CONNECTION";

    [Fact]
    public void Structured_account_name_columns_match_the_domain_contract()
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        using var database = new ToklongDbContext(options);

        var buyer = database.Model.FindEntityType(typeof(BuyerAccount))!;
        var seller = database.Model.FindEntityType(typeof(SellerAccount))!;

        Assert.Equal(120,
            buyer.FindProperty(nameof(BuyerAccount.FirstName))!.GetMaxLength());
        Assert.False(
            buyer.FindProperty(nameof(BuyerAccount.FirstName))!.IsNullable);
        Assert.True(
            buyer.FindProperty(nameof(BuyerAccount.NameChangedAt))!.IsNullable);
        Assert.Equal(120,
            seller.FindProperty(nameof(SellerAccount.LastName))!.GetMaxLength());
        Assert.True(
            seller.FindProperty(nameof(SellerAccount.LastName))!.IsNullable);
    }

    [Fact]
    public void Migration_adds_nullable_columns_before_backfill_and_only_requires_buyer_names()
    {
        var operations = MigrationProbe.CreateUpOperations();

        foreach (var column in operations.OfType<AddColumnOperation>()
                     .Where(operation =>
                         operation.Name is "FirstName" or "LastName" &&
                         operation.Table is "buyers" or "sellers"))
        {
            Assert.True(column.IsNullable);
            Assert.Equal(120, column.MaxLength);
        }

        Assert.Contains(operations.OfType<AlterColumnOperation>(), operation =>
            operation.Table == "buyers" &&
            operation.Name == "FirstName" &&
            !operation.IsNullable);
        Assert.Contains(operations.OfType<AlterColumnOperation>(), operation =>
            operation.Table == "buyers" &&
            operation.Name == "LastName" &&
            !operation.IsNullable);
        Assert.DoesNotContain(operations.OfType<AlterColumnOperation>(), operation =>
            operation.Table == "sellers" &&
            operation.Name is "FirstName" or "LastName" &&
            !operation.IsNullable);
    }

    [RequiresPostgreSqlMigrationFixture]
    public async Task Migration_backfills_same_phone_seller_without_rewriting_legacy_display_names()
    {
        await using var database = await PostgreSqlMigrationDatabase.CreateAsync(
            Environment.GetEnvironmentVariable(
                PostgreSqlConnectionStringEnvironmentVariable)!);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync();

        await ExecuteNonQueryAsync(connection, """
            CREATE TABLE buyers (
                "Id" uuid PRIMARY KEY,
                "PhoneNumber" character varying(16) NOT NULL,
                "FullName" character varying(120) NOT NULL
            );
            CREATE TABLE sellers (
                "Id" uuid PRIMARY KEY,
                "PhoneNumber" character varying(16) NOT NULL,
                "DisplayName" character varying(120) NOT NULL
            );
            INSERT INTO buyers ("Id", "PhoneNumber", "FullName")
            VALUES (
                '10000000-0000-0000-0000-000000000001',
                '+66811111111',
                'ผู้ซื้อ  ทดสอบ เพิ่มเติม');
            INSERT INTO sellers ("Id", "PhoneNumber", "DisplayName")
            VALUES
                ('20000000-0000-0000-0000-000000000001',
                 '+66811111111', 'ผู้ขายเก่า 1234'),
                ('20000000-0000-0000-0000-000000000002',
                 '+66822222222', 'ผู้ขาย 1234');
            """);

        await ApplyUpMigrationAsync(connection);

        await using var command = new NpgsqlCommand("""
            SELECT "FullName", "FirstName", "LastName"
            FROM buyers
            WHERE "Id" = '10000000-0000-0000-0000-000000000001';

            SELECT "DisplayName", "FirstName", "LastName"
            FROM sellers
            ORDER BY "Id";
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("ผู้ซื้อ  ทดสอบ เพิ่มเติม", reader.GetString(0));
        Assert.Equal("ผู้ซื้อ", reader.GetString(1));
        Assert.Equal("ทดสอบ เพิ่มเติม", reader.GetString(2));

        Assert.True(await reader.NextResultAsync());
        Assert.True(await reader.ReadAsync());
        Assert.Equal("ผู้ขายเก่า 1234", reader.GetString(0));
        Assert.Equal("ผู้ซื้อ", reader.GetString(1));
        Assert.Equal("ทดสอบ เพิ่มเติม", reader.GetString(2));
        Assert.True(await reader.ReadAsync());
        Assert.Equal("ผู้ขาย 1234", reader.GetString(0));
        Assert.True(reader.IsDBNull(1));
        Assert.True(reader.IsDBNull(2));
    }

    private static async Task ApplyUpMigrationAsync(NpgsqlConnection connection)
    {
        var options = new DbContextOptionsBuilder<ToklongDbContext>()
            .UseNpgsql(connection.ConnectionString)
            .Options;
        await using var context = new ToklongDbContext(options);
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(
            MigrationProbe.CreateUpOperations(), context.Model);

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

    private static async Task ExecuteNonQueryAsync(
        NpgsqlConnection connection,
        string commandText)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class MigrationProbe : StructuredAccountNames
    {
        public static IReadOnlyList<MigrationOperation> CreateUpOperations()
        {
            var builder = new MigrationBuilder(
                "Npgsql.EntityFrameworkCore.PostgreSQL");
            new MigrationProbe().Up(builder);
            return builder.Operations;
        }
    }

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
            var databaseName = $"toklong_name_migration_{Guid.NewGuid():N}";
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
