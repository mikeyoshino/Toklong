using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TwoPartyAgreementAcceptances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgreementCoreSnapshotCreatedAt",
                table: "transactions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgreementCoreSnapshotHash",
                table: "transactions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgreementCoreSnapshotJson",
                table: "transactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "agreement_acceptances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedPhoneNumber = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    AuthenticationMethod = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AgreementCoreSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TermsVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    TermsSnapshotHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AcceptedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_acceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_agreement_acceptances_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_agreement_acceptances_TransactionId_IdempotencyKey",
                table: "agreement_acceptances",
                columns: new[] { "TransactionId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_agreement_acceptances_TransactionId_Role",
                table: "agreement_acceptances",
                columns: new[] { "TransactionId", "Role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_acceptances");

            migrationBuilder.DropColumn(
                name: "AgreementCoreSnapshotCreatedAt",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "AgreementCoreSnapshotHash",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "AgreementCoreSnapshotJson",
                table: "transactions");
        }
    }
}
