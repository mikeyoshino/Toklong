using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SellerAccessToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BuyerAccessToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SellerDisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    SellerContact = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Condition = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    KnownDefects = table.Column<string>(type: "text", nullable: false),
                    PhotoUrl = table.Column<string>(type: "text", nullable: false),
                    PriceSatang = table.Column<long>(type: "bigint", nullable: false),
                    ShippingFeeSatang = table.Column<long>(type: "bigint", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ShipByDurationHours = table.Column<int>(type: "integer", nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    SellerAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BuyerDisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    BuyerContact = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: true),
                    DeliveryAddress = table.Column<string>(type: "text", nullable: true),
                    BuyerAcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ProductSnapshotJson = table.Column<string>(type: "text", nullable: true),
                    ProductSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ShipByAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PaymentProvider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    PaymentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PaymentConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CarrierCode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    TrackingNumber = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    TrackingSubmittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeliveryEventId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    DisputeWindowEndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BuyerConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DisputeReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    DisputeStatement = table.Column<string>(type: "text", nullable: true),
                    DisputeOpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    PayoutReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    PayoutConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorRole = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ActorId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    FromState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ToState = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    MetadataJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_audit_events_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "external_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    EventId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_events_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_TransactionId_IdempotencyKey",
                table: "audit_events",
                columns: new[] { "TransactionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_events_Provider_EventId",
                table: "external_events",
                columns: new[] { "Provider", "EventId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_external_events_TransactionId",
                table: "external_events",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_transactions_BuyerAccessToken",
                table: "transactions",
                column: "BuyerAccessToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_PaymentReference",
                table: "transactions",
                column: "PaymentReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_PublicToken",
                table: "transactions",
                column: "PublicToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_transactions_SellerAccessToken",
                table: "transactions",
                column: "SellerAccessToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "external_events");

            migrationBuilder.DropTable(
                name: "transactions");
        }
    }
}
