using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Toklong.Infrastructure.Persistence.Migrations;

public partial class AllowSupersededOutboundBookingIntent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_managed_shipments_TransactionId_Direction",
            table: "managed_shipments");
        migrationBuilder.CreateIndex(
            name: "IX_managed_shipments_TransactionId_Direction",
            table: "managed_shipments",
            columns: new[] { "TransactionId", "Direction" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_managed_shipments_TransactionId_Direction",
            table: "managed_shipments");
        migrationBuilder.CreateIndex(
            name: "IX_managed_shipments_TransactionId_Direction",
            table: "managed_shipments",
            columns: new[] { "TransactionId", "Direction" },
            unique: true);
    }
}
