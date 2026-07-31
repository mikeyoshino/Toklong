using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    // Preserves legacy HMAC audit references while adding protected evidence.
    /// <inheritdoc />
    public partial class HardenAccountNameVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "NewName",
                table: "account_name_change_audit_events",
                newName: "LegacyNewNameDigest");

            migrationBuilder.RenameColumn(
                name: "OldName",
                table: "account_name_change_audit_events",
                newName: "LegacyOldNameDigest");

            migrationBuilder.AlterColumn<string>(
                name: "LegacyNewNameDigest",
                table: "account_name_change_audit_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "LegacyOldNameDigest",
                table: "account_name_change_audit_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProtectedNameEvidence",
                table: "account_name_change_audit_events",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectionVersion",
                table: "account_name_change_audit_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "legacy-hmac:v1");

            migrationBuilder.CreateTable(
                name: "account_name_verification_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengeId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SubmittedDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderVerificationKey = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderChallengeId = table.Column<string>(type: "character varying(800)", maxLength: 800, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProviderRequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderCompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderObservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_name_verification_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_name_verification_operations_account_name_change_ch~",
                        column: x => x.ChallengeId,
                        principalTable: "account_name_change_challenges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_name_verification_operations_ChallengeId_Idempotenc~",
                table: "account_name_verification_operations",
                columns: new[] { "ChallengeId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_account_name_verification_operations_ProviderVerificationKey",
                table: "account_name_verification_operations",
                column: "ProviderVerificationKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_name_verification_operations");

            migrationBuilder.AlterColumn<string>(
                name: "LegacyNewNameDigest",
                table: "account_name_change_audit_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LegacyOldNameDigest",
                table: "account_name_change_audit_events",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ProtectedNameEvidence",
                table: "account_name_change_audit_events");

            migrationBuilder.DropColumn(
                name: "ProtectionVersion",
                table: "account_name_change_audit_events");

            migrationBuilder.RenameColumn(
                name: "LegacyNewNameDigest",
                table: "account_name_change_audit_events",
                newName: "NewName");

            migrationBuilder.RenameColumn(
                name: "LegacyOldNameDigest",
                table: "account_name_change_audit_events",
                newName: "OldName");
        }
    }
}
