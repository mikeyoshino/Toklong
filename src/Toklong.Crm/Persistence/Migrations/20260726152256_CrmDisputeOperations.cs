using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmDisputeOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_events",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_account_events_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_account_events_users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispute_cases",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseNumber = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    AssignedUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AssignmentDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FirstReviewDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReadyForApprovalAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ApprovalDueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dispute_cases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_dispute_cases_users_AssignedUserId",
                        column: x => x.AssignedUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_change_requests",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_change_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_role_change_requests_roles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "crm",
                        principalTable: "roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_change_requests_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_change_requests_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_change_requests_users_TargetUserId",
                        column: x => x.TargetUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_events",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MetadataJson = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_case_events_dispute_cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "crm",
                        principalTable: "dispute_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_case_events_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "case_notes",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_case_notes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_case_notes_dispute_cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "crm",
                        principalTable: "dispute_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_case_notes_users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "evidence_requests",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Party = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequiredEvidence = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DueAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_evidence_requests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_evidence_requests_dispute_cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "crm",
                        principalTable: "dispute_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_evidence_requests_users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resolution_actions",
                schema: "crm",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReasonCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RecommendedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecommendedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReference = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resolution_actions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_resolution_actions_dispute_cases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "crm",
                        principalTable: "dispute_cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resolution_actions_users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_resolution_actions_users_RecommendedByUserId",
                        column: x => x.RecommendedByUserId,
                        principalSchema: "crm",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_events_ActorUserId",
                schema: "crm",
                table: "account_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_account_events_TargetUserId_CreatedAt",
                schema: "crm",
                table: "account_events",
                columns: new[] { "TargetUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_case_events_ActorUserId",
                schema: "crm",
                table: "case_events",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_case_events_CaseId_CreatedAt",
                schema: "crm",
                table: "case_events",
                columns: new[] { "CaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_case_events_IdempotencyKey",
                schema: "crm",
                table: "case_events",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_case_notes_AuthorUserId",
                schema: "crm",
                table: "case_notes",
                column: "AuthorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_case_notes_CaseId_CreatedAt",
                schema: "crm",
                table: "case_notes",
                columns: new[] { "CaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dispute_cases_AssignedUserId",
                schema: "crm",
                table: "dispute_cases",
                column: "AssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_dispute_cases_CaseNumber",
                schema: "crm",
                table: "dispute_cases",
                column: "CaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dispute_cases_Status_OpenedAt",
                schema: "crm",
                table: "dispute_cases",
                columns: new[] { "Status", "OpenedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_dispute_cases_TransactionId",
                schema: "crm",
                table: "dispute_cases",
                column: "TransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_evidence_requests_CaseId_DueAt",
                schema: "crm",
                table: "evidence_requests",
                columns: new[] { "CaseId", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_evidence_requests_RequestedByUserId",
                schema: "crm",
                table: "evidence_requests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_resolution_actions_ApprovedByUserId",
                schema: "crm",
                table: "resolution_actions",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_resolution_actions_CaseId_Status",
                schema: "crm",
                table: "resolution_actions",
                columns: new[] { "CaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_resolution_actions_IdempotencyKey",
                schema: "crm",
                table: "resolution_actions",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resolution_actions_RecommendedByUserId",
                schema: "crm",
                table: "resolution_actions",
                column: "RecommendedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_role_change_requests_ApprovedByUserId",
                schema: "crm",
                table: "role_change_requests",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_role_change_requests_RequestedByUserId",
                schema: "crm",
                table: "role_change_requests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_role_change_requests_RoleId",
                schema: "crm",
                table: "role_change_requests",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_role_change_requests_TargetUserId_RoleId_Status",
                schema: "crm",
                table: "role_change_requests",
                columns: new[] { "TargetUserId", "RoleId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_events",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "case_events",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "case_notes",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "evidence_requests",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "resolution_actions",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "role_change_requests",
                schema: "crm");

            migrationBuilder.DropTable(
                name: "dispute_cases",
                schema: "crm");
        }
    }
}
