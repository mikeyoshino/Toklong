using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BuyerSavedDeliveryAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SavedAddressLine",
                table: "buyers",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SavedAddressUpdatedAt",
                table: "buyers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedDistrictId",
                table: "buyers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedDistrictName",
                table: "buyers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedPostalCode",
                table: "buyers",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedProvinceId",
                table: "buyers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedProvinceName",
                table: "buyers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SavedSubdistrictId",
                table: "buyers",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SavedSubdistrictName",
                table: "buyers",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SavedAddressLine",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedAddressUpdatedAt",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedDistrictId",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedDistrictName",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedPostalCode",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedProvinceId",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedProvinceName",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedSubdistrictId",
                table: "buyers");

            migrationBuilder.DropColumn(
                name: "SavedSubdistrictName",
                table: "buyers");
        }
    }
}
