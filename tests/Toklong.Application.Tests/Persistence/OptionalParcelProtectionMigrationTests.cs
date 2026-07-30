using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Toklong.Infrastructure.Persistence.Migrations;

namespace Toklong.Application.Tests.Persistence;

public sealed class OptionalParcelProtectionMigrationTests
{
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
            "WHERE \"ParcelInsuranceFeeSatang\" > 0",
            backfill.Sql,
            StringComparison.Ordinal);
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
}
