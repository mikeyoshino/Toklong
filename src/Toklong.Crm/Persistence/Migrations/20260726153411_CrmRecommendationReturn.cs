using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Crm.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CrmRecommendationReturn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnedAt",
                schema: "crm",
                table: "resolution_actions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReturnedByUserId",
                schema: "crm",
                table: "resolution_actions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReturnedReason",
                schema: "crm",
                table: "resolution_actions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_resolution_actions_ReturnedByUserId",
                schema: "crm",
                table: "resolution_actions",
                column: "ReturnedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_resolution_actions_users_ReturnedByUserId",
                schema: "crm",
                table: "resolution_actions",
                column: "ReturnedByUserId",
                principalSchema: "crm",
                principalTable: "users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_resolution_actions_users_ReturnedByUserId",
                schema: "crm",
                table: "resolution_actions");

            migrationBuilder.DropIndex(
                name: "IX_resolution_actions_ReturnedByUserId",
                schema: "crm",
                table: "resolution_actions");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                schema: "crm",
                table: "resolution_actions");

            migrationBuilder.DropColumn(
                name: "ReturnedByUserId",
                schema: "crm",
                table: "resolution_actions");

            migrationBuilder.DropColumn(
                name: "ReturnedReason",
                schema: "crm",
                table: "resolution_actions");
        }
    }
}
