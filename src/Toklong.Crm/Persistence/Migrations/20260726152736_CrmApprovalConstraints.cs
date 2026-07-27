using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmApprovalConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_role_change_requests_distinct_approver",
                schema: "crm",
                table: "role_change_requests",
                sql: "\"ApprovedByUserId\" IS NULL OR \"ApprovedByUserId\" <> \"RequestedByUserId\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_resolution_actions_distinct_approver",
                schema: "crm",
                table: "resolution_actions",
                sql: "\"ApprovedByUserId\" IS NULL OR \"ApprovedByUserId\" <> \"RecommendedByUserId\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_role_change_requests_distinct_approver",
                schema: "crm",
                table: "role_change_requests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_resolution_actions_distinct_approver",
                schema: "crm",
                table: "resolution_actions");
        }
    }
}
