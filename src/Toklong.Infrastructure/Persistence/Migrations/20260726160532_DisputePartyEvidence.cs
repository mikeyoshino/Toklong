using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DisputePartyEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_retention_file_deletions",
                table: "retention_file_deletions");

            migrationBuilder.AddColumn<Guid>(
                name: "Id",
                table: "retention_file_deletions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE retention_file_deletions
                SET "Id" = md5("TransactionId"::text || '|' || "FileReference")::uuid
                WHERE "Id" IS NULL;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "Id",
                table: "retention_file_deletions",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActionDeadlineAt",
                table: "notification_outbox",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Detail",
                table: "notification_outbox",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_retention_file_deletions",
                table: "retention_file_deletions",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "dispute_evidence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Party = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SubmittedById = table.Column<Guid>(type: "uuid", nullable: false),
                    EvidenceType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    StorageReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    LengthBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispute_evidence", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dispute_evidence_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_retention_file_deletions_TransactionId_FileReference",
                table: "retention_file_deletions",
                columns: new[] { "TransactionId", "FileReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dispute_evidence_TransactionId_Party_IdempotencyKey",
                table: "dispute_evidence",
                columns: new[] { "TransactionId", "Party", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dispute_evidence_TransactionId_Party_SubmittedAt",
                table: "dispute_evidence",
                columns: new[] { "TransactionId", "Party", "SubmittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dispute_evidence");

            migrationBuilder.DropPrimaryKey(
                name: "PK_retention_file_deletions",
                table: "retention_file_deletions");

            migrationBuilder.DropIndex(
                name: "IX_retention_file_deletions_TransactionId_FileReference",
                table: "retention_file_deletions");

            migrationBuilder.DropColumn(
                name: "Id",
                table: "retention_file_deletions");

            migrationBuilder.DropColumn(
                name: "ActionDeadlineAt",
                table: "notification_outbox");

            migrationBuilder.DropColumn(
                name: "Detail",
                table: "notification_outbox");

            migrationBuilder.AddPrimaryKey(
                name: "PK_retention_file_deletions",
                table: "retention_file_deletions",
                column: "TransactionId");
        }
    }
}
