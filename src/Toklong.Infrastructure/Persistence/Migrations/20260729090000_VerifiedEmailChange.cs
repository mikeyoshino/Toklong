using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VerifiedEmailChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "buyer_email_change_audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DestinationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MaskedDestination = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buyer_email_change_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_buyer_email_change_audit_events_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "buyer_email_change_challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PendingEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    MaskedPendingEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    CodeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestIdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ResendAvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SendAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SendFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerificationIdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    IncorrectAttempts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buyer_email_change_challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_buyer_email_change_challenges_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_buyer_email_change_audit_events_BuyerId",
                table: "buyer_email_change_audit_events",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_buyer_email_change_audit_events_ChallengeId",
                table: "buyer_email_change_audit_events",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_buyer_email_change_challenges_BuyerId",
                table: "buyer_email_change_challenges",
                column: "BuyerId",
                unique: true,
                filter: "\"Status\" IN ('PendingSend', 'Active')");

            migrationBuilder.CreateIndex(
                name: "IX_buyer_email_change_challenges_BuyerId_RequestIdempotencyKey",
                table: "buyer_email_change_challenges",
                columns: new[] { "BuyerId", "RequestIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_buyer_email_change_challenges_ExpiresAt",
                table: "buyer_email_change_challenges",
                column: "ExpiresAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "buyer_email_change_audit_events");

            migrationBuilder.DropTable(
                name: "buyer_email_change_challenges");
        }
    }
}
