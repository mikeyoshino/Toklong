using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SellerShippingQuoteSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BuyerTotalSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "PackageHeightCentimeters",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackageLengthCentimeters",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackageWeightGrams",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PackageWidthCentimeters",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ShippingFeeSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ShippingOriginAddress",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingOriginPostalCode",
                table: "transactions",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingOriginProvinceName",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippingQuoteExpiresAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingQuoteProvider",
                table: "transactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingQuoteReference",
                table: "transactions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingServiceCode",
                table: "transactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingServiceName",
                table: "transactions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedShippingAddressLine",
                table: "sellers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SavedShippingAddressUpdatedAt",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedShippingDistrictId",
                table: "sellers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedShippingDistrictName",
                table: "sellers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedShippingPostalCode",
                table: "sellers",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedShippingProvinceId",
                table: "sellers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedShippingProvinceName",
                table: "sellers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedShippingSubdistrictId",
                table: "sellers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedShippingSubdistrictName",
                table: "sellers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "BuyerTotalSatang",
                table: "financial_retention_records",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ShippingFeeSatang",
                table: "financial_retention_records",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE "transactions"
                SET "BuyerTotalSatang" = "PriceSatang"
                WHERE "BuyerTotalSatang" = 0;
                """);

            migrationBuilder.Sql(
                """
                UPDATE "financial_retention_records"
                SET "BuyerTotalSatang" = "PriceSatang"
                WHERE "BuyerTotalSatang" = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerTotalSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PackageHeightCentimeters",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PackageLengthCentimeters",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PackageWeightGrams",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PackageWidthCentimeters",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingFeeSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingOriginAddress",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingOriginPostalCode",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingOriginProvinceName",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingQuoteExpiresAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingQuoteProvider",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingQuoteReference",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingServiceCode",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingServiceName",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SavedShippingAddressLine",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingAddressUpdatedAt",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingDistrictId",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingDistrictName",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingPostalCode",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingProvinceId",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingProvinceName",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingSubdistrictId",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "SavedShippingSubdistrictName",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "BuyerTotalSatang",
                table: "financial_retention_records");

            migrationBuilder.DropColumn(
                name: "ShippingFeeSatang",
                table: "financial_retention_records");
        }
    }
}
