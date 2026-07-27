using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TransactionRetentionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LegalHoldPlacedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReason",
                table: "transactions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegalHoldReference",
                table: "transactions",
                type: "character varying(160)",
                maxLength: 160,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetentionExpiresAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetentionStartsAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "financial_retention_records",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TerminalState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PriceSatang = table.Column<long>(type: "bigint", nullable: false),
                    PlatformFeeSatang = table.Column<long>(type: "bigint", nullable: false),
                    SellerExpectedNetSatang = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    PaymentProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RefundReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PayoutProvider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    PayoutReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    RetentionStartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EvidenceRetentionExpiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinancialRetentionExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurgedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_financial_retention_records", x => x.TransactionId);
                });

            migrationBuilder.CreateTable(
                name: "retention_file_deletions",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_retention_file_deletions", x => x.TransactionId);
                });

            migrationBuilder.Sql(
                """
                WITH terminal_times AS (
                    SELECT
                        t."Id",
                        GREATEST(
                            CASE
                                WHEN t."State" = 'PaidOut'
                                    THEN COALESCE(
                                        t."PayoutConfirmedAt",
                                        audit."OccurredAt",
                                        t."CreatedAt")
                                WHEN t."State" = 'Refunded'
                                    THEN COALESCE(
                                        t."RefundConfirmedAt",
                                        audit."OccurredAt",
                                        t."CreatedAt")
                                ELSE COALESCE(
                                    audit."OccurredAt",
                                    t."CreatedAt")
                            END,
                            t."DisputeResolvedAt"
                        ) AS "RetentionStart"
                    FROM transactions AS t
                    LEFT JOIN LATERAL (
                        SELECT MAX(a."CreatedAt") AS "OccurredAt"
                        FROM audit_events AS a
                        WHERE a."TransactionId" = t."Id"
                    ) AS audit ON TRUE
                    WHERE t."State" IN (
                        'PaidOut',
                        'Refunded',
                        'Cancelled',
                        'Expired')
                )
                UPDATE transactions AS t
                SET
                    "RetentionStartsAt" =
                        terminal_times."RetentionStart",
                    "RetentionExpiresAt" =
                        terminal_times."RetentionStart" +
                        INTERVAL '5 years'
                FROM terminal_times
                WHERE t."Id" = terminal_times."Id";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_RetentionExpiresAt_LegalHoldPlacedAt",
                table: "transactions",
                columns: new[] { "RetentionExpiresAt", "LegalHoldPlacedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_financial_retention_records_FinancialRetentionExpiresAt",
                table: "financial_retention_records",
                column: "FinancialRetentionExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_retention_file_deletions_QueuedAt",
                table: "retention_file_deletions",
                column: "QueuedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "financial_retention_records");

            migrationBuilder.DropTable(
                name: "retention_file_deletions");

            migrationBuilder.DropIndex(
                name: "IX_transactions_RetentionExpiresAt_LegalHoldPlacedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "LegalHoldPlacedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "LegalHoldReason",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "LegalHoldReference",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "RetentionExpiresAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "RetentionStartsAt",
                table: "transactions");
        }
    }
}
