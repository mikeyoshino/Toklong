using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResolveShippingAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ResolutionCode",
                table: "provider_shipping_adjustments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ResolvedAt",
                table: "provider_shipping_adjustments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResolvedBy",
                table: "provider_shipping_adjustments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "Version",
                table: "provider_shipping_adjustments",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResolutionCode",
                table: "provider_shipping_adjustments");

            migrationBuilder.DropColumn(
                name: "ResolvedAt",
                table: "provider_shipping_adjustments");

            migrationBuilder.DropColumn(
                name: "ResolvedBy",
                table: "provider_shipping_adjustments");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "provider_shipping_adjustments");
        }
    }
}
