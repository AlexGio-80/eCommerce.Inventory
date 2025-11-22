using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderItemBlueprintRelationNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "BlueprintId",
                table: "OrderItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            // Set all invalid BlueprintId values to NULL
            migrationBuilder.Sql(@"
                UPDATE oi 
                SET oi.BlueprintId = NULL 
                FROM OrderItems oi 
                LEFT JOIN Blueprints b ON oi.BlueprintId = b.Id 
                WHERE oi.BlueprintId IS NOT NULL AND b.Id IS NULL
            ");

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_BlueprintId",
                table: "OrderItems",
                column: "BlueprintId");

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_Blueprints_BlueprintId",
                table: "OrderItems",
                column: "BlueprintId",
                principalTable: "Blueprints",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_Blueprints_BlueprintId",
                table: "OrderItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_BlueprintId",
                table: "OrderItems");

            migrationBuilder.AlterColumn<int>(
                name: "BlueprintId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
