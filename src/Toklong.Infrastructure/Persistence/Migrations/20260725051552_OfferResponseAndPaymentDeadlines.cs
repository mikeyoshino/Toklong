using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfferResponseAndPaymentDeadlines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BuyerPaymentDeadlineAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpirationReason",
                table: "transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SellerAcceptanceDeadlineAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.Sql(
                """
                UPDATE transactions
                SET "SellerAcceptanceDeadlineAt" =
                    "ActivatedAt" + INTERVAL '24 hours';

                UPDATE transactions
                SET "BuyerPaymentDeadlineAt" =
                    "SellerAcceptedAt" + INTERVAL '1 hour'
                WHERE "SellerAcceptedAt" IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BuyerPaymentDeadlineAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ExpirationReason",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SellerAcceptanceDeadlineAt",
                table: "transactions");
        }
    }
}
