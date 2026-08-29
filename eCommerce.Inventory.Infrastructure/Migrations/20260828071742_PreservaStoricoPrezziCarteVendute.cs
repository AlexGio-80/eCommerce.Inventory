using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PreservaStoricoPrezziCarteVendute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceChangeLogs_InventoryItems_InventoryItemId",
                table: "PriceChangeLogs");

            migrationBuilder.AlterColumn<int>(
                name: "InventoryItemId",
                table: "PriceChangeLogs",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddForeignKey(
                name: "FK_PriceChangeLogs_InventoryItems_InventoryItemId",
                table: "PriceChangeLogs",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PriceChangeLogs_InventoryItems_InventoryItemId",
                table: "PriceChangeLogs");

            migrationBuilder.AlterColumn<int>(
                name: "InventoryItemId",
                table: "PriceChangeLogs",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PriceChangeLogs_InventoryItems_InventoryItemId",
                table: "PriceChangeLogs",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
