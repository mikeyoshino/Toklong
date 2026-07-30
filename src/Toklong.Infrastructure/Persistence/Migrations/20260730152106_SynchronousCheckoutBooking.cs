using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SynchronousCheckoutBooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "booking_attempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProviderPurchaseId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ProviderTrackingCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CourierTrackingCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    QuotedShippingFeeSatang = table.Column<long>(type: "bigint", nullable: true),
                    QuotedProtectionFeeSatang = table.Column<long>(type: "bigint", nullable: true),
                    QuotedCoverageLimitSatang = table.Column<long>(type: "bigint", nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    ProviderResponseFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureCategory = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    SafeFailureCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_booking_attempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_booking_attempts_managed_shipments_ManagedShipmentId",
                        column: x => x.ManagedShipmentId,
                        principalTable: "managed_shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_booking_attempts_ManagedShipmentId",
                table: "booking_attempts",
                column: "ManagedShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_booking_attempts_ProviderReference",
                table: "booking_attempts",
                column: "ProviderReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_attempts_TransactionId",
                table: "booking_attempts",
                column: "TransactionId",
                unique: true,
                filter: "\"Status\" IN ('Created', 'CallingProvider')");

            migrationBuilder.CreateIndex(
                name: "IX_booking_attempts_TransactionId_AttemptNumber",
                table: "booking_attempts",
                columns: new[] { "TransactionId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_attempts_TransactionId_IdempotencyKey",
                table: "booking_attempts",
                columns: new[] { "TransactionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_booking_attempts_TransactionId_Status_CreatedAt",
                table: "booking_attempts",
                columns: new[] { "TransactionId", "Status", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "booking_attempts");
        }
    }
}
