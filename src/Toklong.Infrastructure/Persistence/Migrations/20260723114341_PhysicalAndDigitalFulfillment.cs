using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PhysicalAndDigitalFulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DigitalDeliveryStatement",
                table: "transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DigitalDeliverySubmittedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DigitalManualReviewReference",
                table: "transactions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FulfillmentType",
                table: "transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "PhysicalShipment");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigitalDeliveryStatement",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DigitalDeliverySubmittedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DigitalManualReviewReference",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "FulfillmentType",
                table: "transactions");
        }
    }
}
