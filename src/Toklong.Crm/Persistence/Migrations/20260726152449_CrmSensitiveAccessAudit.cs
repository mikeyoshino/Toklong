using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmSensitiveAccessAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_assignments",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssigneeUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AssignedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_case_assignments_dispute_cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "crm",
                        principalTable: "dispute_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_case_assignments_users_AssignedByUserId",
                        column: x => x.AssignedByUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_case_assignments_users_AssigneeUserId",
                        column: x => x.AssigneeUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sensitive_access_events",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceType = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    ResourceReference = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    CorrelationId = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sensitive_access_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sensitive_access_events_dispute_cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "crm",
                        principalTable: "dispute_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sensitive_access_events_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_case_assignments_AssignedByUserId",
                schema: "crm",
                table: "case_assignments",
                column: "AssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_case_assignments_AssigneeUserId",
                schema: "crm",
                table: "case_assignments",
                column: "AssigneeUserId");

            migrationBuilder.CreateIndex(
                name: "IX_case_assignments_CaseId_AssignedAt",
                schema: "crm",
                table: "case_assignments",
                columns: new[] { "CaseId", "AssignedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sensitive_access_events_ActorUserId",
                schema: "crm",
                table: "sensitive_access_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_sensitive_access_events_CaseId_CreatedAt",
                schema: "crm",
                table: "sensitive_access_events",
                columns: new[] { "CaseId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_assignments",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "sensitive_access_events",
                schema: "crm");
        }
    }
}
