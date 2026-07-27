using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuyerFirstOfferFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE transactions DROP COLUMN IF EXISTS "ShippingFeeSatang";""");

            migrationBuilder.AlterColumn<Guid>(
                name: "SellerId",
                table: "transactions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SellerAcceptedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.Sql(
                """
                ALTER TABLE transactions
                ADD COLUMN IF NOT EXISTS "InitiatorRole" character varying(20) NOT NULL DEFAULT 'Seller';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE transactions DROP COLUMN IF EXISTS "InitiatorRole";""");

            migrationBuilder.AlterColumn<Guid>(
                name: "SellerId",
                table: "transactions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "SellerAcceptedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.Sql(
                """
                ALTER TABLE transactions
                ADD COLUMN IF NOT EXISTS "ShippingFeeSatang" bigint NOT NULL DEFAULT 0;
                """);
        }
    }
}
