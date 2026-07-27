using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MobileApiSecurityAndStripePayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FeePolicyVersion",
                table: "transactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "PlatformFeeSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "SellerExpectedNetSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.Sql(
                """
                UPDATE transactions
                SET "SellerExpectedNetSatang" = "PriceSatang",
                    "FeePolicyVersion" = 'legacy-unconfigured'
                """);

            migrationBuilder.CreateTable(
                name: "mobile_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RefreshTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastRotatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_sessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_sessions_BuyerId",
                table: "mobile_sessions",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_mobile_sessions_RefreshTokenHash",
                table: "mobile_sessions",
                column: "RefreshTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_sessions_SellerId",
                table: "mobile_sessions",
                column: "SellerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_sessions");

            migrationBuilder.DropColumn(
                name: "FeePolicyVersion",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PlatformFeeSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SellerExpectedNetSatang",
                table: "transactions");
        }
    }
}
