using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ParcelProtectionRebooking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_managed_shipments_TransactionId_Direction",
                table: "managed_shipments");

            migrationBuilder.CreateTable(
                name: "parcel_protection_change_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousManagedShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DesiredElection = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DesiredCustomerPriceSatang = table.Column<long>(type: "bigint", nullable: false),
                    DesiredProviderCostSatang = table.Column<long>(type: "bigint", nullable: false),
                    DesiredServiceFeeSatang = table.Column<long>(type: "bigint", nullable: false),
                    DesiredIncludedCoverageSatang = table.Column<long>(type: "bigint", nullable: false),
                    DesiredSelectedCoverageSatang = table.Column<long>(type: "bigint", nullable: false),
                    DesiredTermsVersion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    DesiredOptionReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    DesiredInsuranceCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    DesiredQuotedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DesiredExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parcel_protection_change_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_parcel_protection_change_requests_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_shipments_TransactionId_Direction_Status",
                table: "managed_shipments",
                columns: new[] { "TransactionId", "Direction", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_parcel_protection_change_requests_IdempotencyKey",
                table: "parcel_protection_change_requests",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_parcel_protection_change_requests_TransactionId_Status",
                table: "parcel_protection_change_requests",
                columns: new[] { "TransactionId", "Status" },
                unique: true,
                filter: "\"Status\" IN ('AwaitingCancellation', 'AwaitingRebooking')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_protection_change_requests");

            migrationBuilder.DropIndex(
                name: "IX_managed_shipments_TransactionId_Direction_Status",
                table: "managed_shipments");

            migrationBuilder.CreateIndex(
                name: "IX_managed_shipments_TransactionId_Direction",
                table: "managed_shipments",
                columns: new[] { "TransactionId", "Direction" });
        }
    }
}
