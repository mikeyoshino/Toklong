using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShippopManagedShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddressLine",
                table: "transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryDistrictName",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliverySubdistrictName",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippingCancelledAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippingConfirmedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingCourierTrackingCode",
                table: "transactions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingLastProviderStatus",
                table: "transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippingLastReconciledAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingOriginAddressLine",
                table: "transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingOriginDistrictName",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingOriginSubdistrictName",
                table: "transactions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingProviderTrackingCode",
                table: "transactions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingPurchaseReference",
                table: "transactions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippingReservedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_ShippingProviderTrackingCode",
                table: "transactions",
                column: "ShippingProviderTrackingCode",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_transactions_ShippingProviderTrackingCode",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DeliveryAddressLine",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DeliveryDistrictName",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DeliverySubdistrictName",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingCancelledAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingConfirmedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingCourierTrackingCode",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingLastProviderStatus",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingLastReconciledAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingOriginAddressLine",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingOriginDistrictName",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingOriginSubdistrictName",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingProviderTrackingCode",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingPurchaseReference",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingReservedAt",
                table: "transactions");
        }
    }
}
