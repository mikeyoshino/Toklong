using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StructuredAccountNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "sellers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "sellers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NameChangedAt",
                table: "sellers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "buyers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "buyers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NameChangedAt",
                table: "buyers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM buyers
                        WHERE strpos(
                            regexp_replace(btrim("FullName"), '\s+', ' ', 'g'),
                            ' ') = 0)
                    THEN
                        RAISE EXCEPTION
                            'Cannot split legacy buyer display name into first and last name';
                    END IF;
                END $$;
                """);

            migrationBuilder.Sql(
                """
                WITH normalized_buyers AS (
                    SELECT
                        "Id",
                        regexp_replace(btrim("FullName"), '\s+', ' ', 'g')
                            AS normalized_name
                    FROM buyers)
                UPDATE buyers AS buyer
                SET
                    "FirstName" = split_part(
                        normalized_buyers.normalized_name, ' ', 1),
                    "LastName" = substr(
                        normalized_buyers.normalized_name,
                        strpos(normalized_buyers.normalized_name, ' ') + 1)
                FROM normalized_buyers
                WHERE buyer."Id" = normalized_buyers."Id";
                """);

            migrationBuilder.Sql(
                """
                UPDATE sellers AS seller
                SET
                    "FirstName" = buyer."FirstName",
                    "LastName" = buyer."LastName"
                FROM buyers AS buyer
                WHERE seller."PhoneNumber" = buyer."PhoneNumber";
                """);

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "buyers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "buyers",
                type: "character varying(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(120)",
                oldMaxLength: 120,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "NameChangedAt",
                table: "sellers");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "NameChangedAt",
                table: "buyers");
        }
    }
}
