using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    // Adds one-time phone-registration evidence without altering paid transactions.
    /// <inheritdoc />
    public partial class PhoneFirstRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mobile_account_terms_acceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mobile_account_terms_acceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_mobile_account_terms_acceptances_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pending_mobile_registrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    InstallationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletionIdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_pending_mobile_registrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_pending_mobile_registrations_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_mobile_account_terms_acceptances_BuyerId_TermsVersion",
                table: "mobile_account_terms_acceptances",
                columns: new[] { "BuyerId", "TermsVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_mobile_account_terms_acceptances_IdempotencyKey",
                table: "mobile_account_terms_acceptances",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_pending_mobile_registrations_BuyerId",
                table: "pending_mobile_registrations",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_pending_mobile_registrations_ExpiresAt",
                table: "pending_mobile_registrations",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_pending_mobile_registrations_TicketHash",
                table: "pending_mobile_registrations",
                column: "TicketHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mobile_account_terms_acceptances");

            migrationBuilder.DropTable(
                name: "pending_mobile_registrations");
        }
    }
}
