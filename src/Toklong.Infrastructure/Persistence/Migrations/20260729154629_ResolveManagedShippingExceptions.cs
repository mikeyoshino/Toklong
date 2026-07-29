using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResolveManagedShippingExceptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExceptionResolutionReference",
                table: "managed_shipments",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ExceptionResolvedAt",
                table: "managed_shipments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionResolvedBy",
                table: "managed_shipments",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExceptionResolutionReference",
                table: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ExceptionResolvedAt",
                table: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ExceptionResolvedBy",
                table: "managed_shipments");
        }
    }
}
