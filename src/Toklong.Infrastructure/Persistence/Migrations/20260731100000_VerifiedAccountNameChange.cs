using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class VerifiedAccountNameChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_name_change_challenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MaskedPhoneNumber = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PendingFirstName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    PendingLastName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    RequestIdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderRequestKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OperationKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceChallengeId = table.Column<Guid>(type: "uuid", nullable: true),
                    OperationFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderChallengeId = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: true),
                    VerificationIdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResendAvailableAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SendAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SendFailedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LockedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IncorrectAttempts = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_name_change_challenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_name_change_challenges_account_name_change_challeng~",
                        column: x => x.SourceChallengeId,
                        principalTable: "account_name_change_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_change_challenges_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_change_challenges_mobile_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "mobile_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_change_challenges_sellers_SellerId",
                        column: x => x.SellerId,
                        principalTable: "sellers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_name_change_audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    NewName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Result = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_name_change_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_name_change_audit_events_account_name_change_challe~",
                        column: x => x.ChallengeId,
                        principalTable: "account_name_change_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_change_audit_events_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_change_audit_events_mobile_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "mobile_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_change_audit_events_sellers_SellerId",
                        column: x => x.SellerId,
                        principalTable: "sellers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "account_name_verification_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SellerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RemainingAttempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_name_verification_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_name_verification_attempts_account_name_change_chal~",
                        column: x => x.ChallengeId,
                        principalTable: "account_name_change_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_verification_attempts_buyers_BuyerId",
                        column: x => x.BuyerId,
                        principalTable: "buyers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_verification_attempts_mobile_sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "mobile_sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_name_verification_attempts_sellers_SellerId",
                        column: x => x.SellerId,
                        principalTable: "sellers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_audit_events_BuyerId",
                table: "account_name_change_audit_events",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_audit_events_ChallengeId",
                table: "account_name_change_audit_events",
                column: "ChallengeId");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_audit_events_SellerId",
                table: "account_name_change_audit_events",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_audit_events_SessionId",
                table: "account_name_change_audit_events",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_BuyerId_SendAcceptedAt",
                table: "account_name_change_challenges",
                columns: new[] { "BuyerId", "SendAcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_PhoneNumber",
                table: "account_name_change_challenges",
                column: "PhoneNumber",
                unique: true,
                filter: "\"Status\" IN ('PendingSend', 'Active')");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_PhoneNumber_RequestIdempoten~",
                table: "account_name_change_challenges",
                columns: new[] { "PhoneNumber", "RequestIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_PhoneNumber_SendAcceptedAt",
                table: "account_name_change_challenges",
                columns: new[] { "PhoneNumber", "SendAcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_ProviderRequestKey",
                table: "account_name_change_challenges",
                column: "ProviderRequestKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_SellerId_SendAcceptedAt",
                table: "account_name_change_challenges",
                columns: new[] { "SellerId", "SendAcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_SessionId",
                table: "account_name_change_challenges",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_change_challenges_SourceChallengeId_RequestIde~",
                table: "account_name_change_challenges",
                columns: new[] { "SourceChallengeId", "RequestIdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_name_verification_attempts_BuyerId",
                table: "account_name_verification_attempts",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_verification_attempts_ChallengeId_IdempotencyK~",
                table: "account_name_verification_attempts",
                columns: new[] { "ChallengeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_name_verification_attempts_SellerId",
                table: "account_name_verification_attempts",
                column: "SellerId");

            migrationBuilder.CreateIndex(
                name: "IX_account_name_verification_attempts_SessionId",
                table: "account_name_verification_attempts",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_name_change_audit_events");

            migrationBuilder.DropTable(
                name: "account_name_verification_attempts");

            migrationBuilder.DropTable(
                name: "account_name_change_challenges");
        }
    }
}
