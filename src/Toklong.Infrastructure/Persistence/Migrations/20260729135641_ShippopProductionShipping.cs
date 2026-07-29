using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShippopProductionShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ParcelInsuranceFeeSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ShippingDeclaredValueSatang",
                table: "transactions",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "ShippingInsuranceCode",
                table: "transactions",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "managed_shipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Direction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OriginPrivateSnapshotReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    DestinationPrivateSnapshotReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ParcelName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    WeightGrams = table.Column<int>(type: "integer", nullable: false),
                    WidthCentimeters = table.Column<int>(type: "integer", nullable: false),
                    LengthCentimeters = table.Column<int>(type: "integer", nullable: false),
                    HeightCentimeters = table.Column<int>(type: "integer", nullable: false),
                    CarrierCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ServiceCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ServiceName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    HandoffMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BaseShippingFeeSatang = table.Column<long>(type: "bigint", nullable: false),
                    InsuranceFeeSatang = table.Column<long>(type: "bigint", nullable: false),
                    DeclaredValueSatang = table.Column<long>(type: "bigint", nullable: false),
                    InsuranceCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    QuoteReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    QuoteExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PurchaseReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ProviderTrackingCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CourierTrackingCode = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ReservedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FirstCarrierScanAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    InTransitAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastProviderStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    LastReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_managed_shipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_managed_shipments_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_shipping_adjustments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    AmountSatang = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ProviderOccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CrmCaseReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_shipping_adjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_provider_shipping_adjustments_managed_shipments_ManagedShip~",
                        column: x => x.ManagedShipmentId,
                        principalTable: "managed_shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_provider_shipping_adjustments_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipping_insurance_cases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ProviderCaseReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeclaredValueSatang = table.Column<long>(type: "bigint", nullable: false),
                    ClaimedAmountSatang = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CrmCaseReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    OpenedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ResolvedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ProviderResultCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderResolutionReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_insurance_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipping_insurance_cases_managed_shipments_ManagedShipmentId",
                        column: x => x.ManagedShipmentId,
                        principalTable: "managed_shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shipping_insurance_cases_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "shipping_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ManagedShipmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProviderPurchaseReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ProviderTrackingReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LeaseOwner = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    LeaseExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSanitizedErrorCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_shipping_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_shipping_operations_managed_shipments_ManagedShipmentId",
                        column: x => x.ManagedShipmentId,
                        principalTable: "managed_shipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_shipping_operations_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_managed_shipments_TransactionId_Direction",
                table: "managed_shipments",
                columns: new[] { "TransactionId", "Direction" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_shipping_adjustments_ManagedShipmentId",
                table: "provider_shipping_adjustments",
                column: "ManagedShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_provider_shipping_adjustments_ProviderReference",
                table: "provider_shipping_adjustments",
                column: "ProviderReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_provider_shipping_adjustments_TransactionId",
                table: "provider_shipping_adjustments",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_insurance_cases_ManagedShipmentId",
                table: "shipping_insurance_cases",
                column: "ManagedShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_insurance_cases_ProviderCaseReference",
                table: "shipping_insurance_cases",
                column: "ProviderCaseReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipping_insurance_cases_TransactionId",
                table: "shipping_insurance_cases",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_operations_IdempotencyKey",
                table: "shipping_operations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_shipping_operations_LeaseExpiresAt",
                table: "shipping_operations",
                column: "LeaseExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_operations_ManagedShipmentId",
                table: "shipping_operations",
                column: "ManagedShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_shipping_operations_Status_NextAttemptAt",
                table: "shipping_operations",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_shipping_operations_TransactionId",
                table: "shipping_operations",
                column: "TransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_shipping_adjustments");

            migrationBuilder.DropTable(
                name: "shipping_insurance_cases");

            migrationBuilder.DropTable(
                name: "shipping_operations");

            migrationBuilder.DropTable(
                name: "managed_shipments");

            migrationBuilder.DropColumn(
                name: "ParcelInsuranceFeeSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingDeclaredValueSatang",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "ShippingInsuranceCode",
                table: "transactions");
        }
    }
}
