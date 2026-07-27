using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AgreementSnapshotAndInspectionEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgreementSnapshotCreatedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgreementSnapshotSealedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeliveryEventReceivedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DisputeWindowStartsAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstCarrierScanAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InTransitAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "InspectionWindowDurationHours",
                table: "transactions",
                type: "integer",
                nullable: false,
                defaultValue: 168);

            migrationBuilder.AddColumn<string>(
                name: "PayoutReleaseReason",
                table: "transactions",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SnapshotSchemaVersion",
                table: "transactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsSnapshotHash",
                table: "transactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TermsSnapshotJson",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TrackingVerificationStatus",
                table: "transactions",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgreementSnapshotCreatedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "AgreementSnapshotSealedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DeliveryEventReceivedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "DisputeWindowStartsAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "FirstCarrierScanAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "InTransitAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "InspectionWindowDurationHours",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "PayoutReleaseReason",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "SnapshotSchemaVersion",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TermsSnapshotHash",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TermsSnapshotJson",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "TrackingVerificationStatus",
                table: "transactions");
        }
    }
}
