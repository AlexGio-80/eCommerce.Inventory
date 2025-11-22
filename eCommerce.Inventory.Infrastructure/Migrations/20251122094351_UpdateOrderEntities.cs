using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateOrderEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_InventoryItems_InventoryItemId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "DatePlaced",
                table: "Orders");

            migrationBuilder.RenameColumn(
                name: "TotalAmount",
                table: "Orders",
                newName: "SellerTotal");

            migrationBuilder.RenameColumn(
                name: "Status",
                table: "Orders",
                newName: "TransactionCode");

            migrationBuilder.RenameColumn(
                name: "ShippingCost",
                table: "Orders",
                newName: "SellerSubtotal");

            migrationBuilder.RenameColumn(
                name: "QuantitySold",
                table: "OrderItems",
                newName: "Quantity");

            migrationBuilder.RenameColumn(
                name: "PricePerItem",
                table: "OrderItems",
                newName: "Price");

            migrationBuilder.AddColumn<string>(
                name: "BillingAddressJson",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "BuyerId",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "BuyerUsername",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "IsCompleted",
                table: "Orders",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PaidAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SellerFee",
                table: "Orders",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "SentAt",
                table: "Orders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingAddressJson",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "Orders",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<int>(
                name: "InventoryItemId",
                table: "OrderItems",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "BlueprintId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CardTraderId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Condition",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ExpansionName",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsAltered",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFoil",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPrepared",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsSigned",
                table: "OrderItems",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UserDataField",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_InventoryItems_InventoryItemId",
                table: "OrderItems",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderItems_InventoryItems_InventoryItemId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "BillingAddressJson",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerId",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BuyerUsername",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "IsCompleted",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "PaidAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SellerFee",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SentAt",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ShippingAddressJson",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "State",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "BlueprintId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "CardTraderId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Condition",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ExpansionName",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsAltered",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsFoil",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsPrepared",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "IsSigned",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "UserDataField",
                table: "OrderItems");

            migrationBuilder.RenameColumn(
                name: "TransactionCode",
                table: "Orders",
                newName: "Status");

            migrationBuilder.RenameColumn(
                name: "SellerTotal",
                table: "Orders",
                newName: "TotalAmount");

            migrationBuilder.RenameColumn(
                name: "SellerSubtotal",
                table: "Orders",
                newName: "ShippingCost");

            migrationBuilder.RenameColumn(
                name: "Quantity",
                table: "OrderItems",
                newName: "QuantitySold");

            migrationBuilder.RenameColumn(
                name: "Price",
                table: "OrderItems",
                newName: "PricePerItem");

            migrationBuilder.AddColumn<DateTime>(
                name: "DatePlaced",
                table: "Orders",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<int>(
                name: "InventoryItemId",
                table: "OrderItems",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OrderItems_InventoryItems_InventoryItemId",
                table: "OrderItems",
                column: "InventoryItemId",
                principalTable: "InventoryItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
