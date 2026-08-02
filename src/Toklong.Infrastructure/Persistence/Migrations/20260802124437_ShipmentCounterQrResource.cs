using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShipmentCounterQrResource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shipment_counter_qr_resources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Representation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProtectedArtifact = table.Column<byte[]>(type: "bytea", nullable: true),
                    ProtectionVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ArtifactSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderResourceDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ProviderExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSanitizedErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LeaseOwner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipment_counter_qr_resources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipment_counter_qr_resources_managed_shipments_ManagedShip~",
                        column: x => x.ManagedShipmentId,
                        principalTable: "managed_shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_shipment_counter_qr_resources_LeaseExpiresAt",
                table: "shipment_counter_qr_resources",
                column: "LeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_shipment_counter_qr_resources_ManagedShipmentId",
                table: "shipment_counter_qr_resources",
                column: "ManagedShipmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipment_counter_qr_resources_Status_NextAttemptAt",
                table: "shipment_counter_qr_resources",
                columns: new[] { "Status", "NextAttemptAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shipment_counter_qr_resources");
        }
    }
}
