using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmCaseRetentionCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_case_assignments_dispute_cases_CaseId",
                schema: "crm",
                table: "case_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_case_events_dispute_cases_CaseId",
                schema: "crm",
                table: "case_events");

            migrationBuilder.DropForeignKey(
                name: "FK_case_notes_dispute_cases_CaseId",
                schema: "crm",
                table: "case_notes");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_requests_dispute_cases_CaseId",
                schema: "crm",
                table: "evidence_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_resolution_actions_dispute_cases_CaseId",
                schema: "crm",
                table: "resolution_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_sensitive_access_events_dispute_cases_CaseId",
                schema: "crm",
                table: "sensitive_access_events");

            migrationBuilder.AddForeignKey(
                name: "FK_case_assignments_dispute_cases_CaseId",
                schema: "crm",
                table: "case_assignments",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_case_events_dispute_cases_CaseId",
                schema: "crm",
                table: "case_events",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_case_notes_dispute_cases_CaseId",
                schema: "crm",
                table: "case_notes",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_requests_dispute_cases_CaseId",
                schema: "crm",
                table: "evidence_requests",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_resolution_actions_dispute_cases_CaseId",
                schema: "crm",
                table: "resolution_actions",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_sensitive_access_events_dispute_cases_CaseId",
                schema: "crm",
                table: "sensitive_access_events",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_dispute_cases_transactions_TransactionId",
                schema: "crm",
                table: "dispute_cases",
                column: "TransactionId",
                principalSchema: "public",
                principalTable: "transactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_dispute_cases_transactions_TransactionId",
                schema: "crm",
                table: "dispute_cases");

            migrationBuilder.DropForeignKey(
                name: "FK_case_assignments_dispute_cases_CaseId",
                schema: "crm",
                table: "case_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_case_events_dispute_cases_CaseId",
                schema: "crm",
                table: "case_events");

            migrationBuilder.DropForeignKey(
                name: "FK_case_notes_dispute_cases_CaseId",
                schema: "crm",
                table: "case_notes");

            migrationBuilder.DropForeignKey(
                name: "FK_evidence_requests_dispute_cases_CaseId",
                schema: "crm",
                table: "evidence_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_resolution_actions_dispute_cases_CaseId",
                schema: "crm",
                table: "resolution_actions");

            migrationBuilder.DropForeignKey(
                name: "FK_sensitive_access_events_dispute_cases_CaseId",
                schema: "crm",
                table: "sensitive_access_events");

            migrationBuilder.AddForeignKey(
                name: "FK_case_assignments_dispute_cases_CaseId",
                schema: "crm",
                table: "case_assignments",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_case_events_dispute_cases_CaseId",
                schema: "crm",
                table: "case_events",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_case_notes_dispute_cases_CaseId",
                schema: "crm",
                table: "case_notes",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_evidence_requests_dispute_cases_CaseId",
                schema: "crm",
                table: "evidence_requests",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_resolution_actions_dispute_cases_CaseId",
                schema: "crm",
                table: "resolution_actions",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sensitive_access_events_dispute_cases_CaseId",
                schema: "crm",
                table: "sensitive_access_events",
                column: "CaseId",
                principalSchema: "crm",
                principalTable: "dispute_cases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
