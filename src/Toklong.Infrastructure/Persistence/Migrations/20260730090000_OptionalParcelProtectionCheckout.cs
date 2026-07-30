using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OptionalParcelProtectionCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ParcelProtectionBuyerElectedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParcelProtectionElection",
                table: "transactions",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ParcelProtectionExpiresAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParcelProtectionIncludedCoverageSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ParcelProtectionOptionReference",
                table: "transactions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParcelProtectionProviderCostSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ParcelProtectionQuotedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParcelProtectionSelectedCoverageSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ParcelProtectionServiceFeeSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ParcelProtectionTermsVersion",
                table: "transactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE transactions
                SET
                  "ParcelProtectionElection" = 'Accepted',
                  "ParcelProtectionProviderCostSatang" = "ParcelInsuranceFeeSatang",
                  "ParcelProtectionServiceFeeSatang" = 0,
                  "ParcelProtectionTermsVersion" = 'legacy-full-value-v1',
                  "ParcelProtectionBuyerElectedAt" = "BuyerAcceptedAt"
                WHERE "ParcelInsuranceFeeSatang" > 0;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "InsuranceCode",
                table: "managed_shipments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80);

            migrationBuilder.AddColumn<string>(
                name: "ParcelProtectionElection",
                table: "managed_shipments",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<long>(
                name: "ParcelProtectionIncludedCoverageSatang",
                table: "managed_shipments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ParcelProtectionOptionReference",
                table: "managed_shipments",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ParcelProtectionProviderCostSatang",
                table: "managed_shipments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ParcelProtectionSelectedCoverageSatang",
                table: "managed_shipments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ParcelProtectionTermsVersion",
                table: "managed_shipments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "parcel-protection-pending-v1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ParcelProtectionBuyerElectedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionElection",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionExpiresAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionIncludedCoverageSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionOptionReference",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionProviderCostSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionQuotedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionSelectedCoverageSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionServiceFeeSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionTermsVersion",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionElection",
                table: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionIncludedCoverageSatang",
                table: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionOptionReference",
                table: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionProviderCostSatang",
                table: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionSelectedCoverageSatang",
                table: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ParcelProtectionTermsVersion",
                table: "managed_shipments");

            migrationBuilder.Sql(
                """
                UPDATE managed_shipments
                SET "InsuranceCode" = ''
                WHERE "InsuranceCode" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "InsuranceCode",
                table: "managed_shipments",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(80)",
                oldMaxLength: 80,
                oldNullable: true);
        }
    }
}
