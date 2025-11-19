using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceBlueprintModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackImageUrl",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CardMarketIds",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Blueprints",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Blueprints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "EditableProperties",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "FixedProperties",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "GameId",
                table: "Blueprints",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ScryfallId",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "TcgPlayerId",
                table: "Blueprints",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Blueprints",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Blueprints_GameId",
                table: "Blueprints",
                column: "GameId");

            migrationBuilder.AddForeignKey(
                name: "FK_Blueprints_Games_GameId",
                table: "Blueprints",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.NoAction);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Blueprints_Games_GameId",
                table: "Blueprints");

            migrationBuilder.DropIndex(
                name: "IX_Blueprints_GameId",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "BackImageUrl",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "CardMarketIds",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "EditableProperties",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "FixedProperties",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "GameId",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "ScryfallId",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "TcgPlayerId",
                table: "Blueprints");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Blueprints");
        }
    }
}
