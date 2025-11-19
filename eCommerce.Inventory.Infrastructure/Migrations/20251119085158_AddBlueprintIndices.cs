using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlueprintIndices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Blueprints_Games_GameId",
                table: "Blueprints");

            migrationBuilder.RenameIndex(
                name: "IX_Blueprints_GameId",
                table: "Blueprints",
                newName: "IX_Blueprint_GameId");

            migrationBuilder.RenameIndex(
                name: "IX_Blueprints_ExpansionId",
                table: "Blueprints",
                newName: "IX_Blueprint_ExpansionId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Blueprints",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Blueprint_CardTraderId",
                table: "Blueprints",
                column: "CardTraderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Blueprint_GameId_ExpansionId",
                table: "Blueprints",
                columns: new[] { "GameId", "ExpansionId" });

            migrationBuilder.CreateIndex(
                name: "IX_Blueprint_Name",
                table: "Blueprints",
                column: "Name");

            migrationBuilder.AddForeignKey(
                name: "FK_Blueprints_Games_GameId",
                table: "Blueprints",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Blueprints_Games_GameId",
                table: "Blueprints");

            migrationBuilder.DropIndex(
                name: "IX_Blueprint_CardTraderId",
                table: "Blueprints");

            migrationBuilder.DropIndex(
                name: "IX_Blueprint_GameId_ExpansionId",
                table: "Blueprints");

            migrationBuilder.DropIndex(
                name: "IX_Blueprint_Name",
                table: "Blueprints");

            migrationBuilder.RenameIndex(
                name: "IX_Blueprint_GameId",
                table: "Blueprints",
                newName: "IX_Blueprints_GameId");

            migrationBuilder.RenameIndex(
                name: "IX_Blueprint_ExpansionId",
                table: "Blueprints",
                newName: "IX_Blueprints_ExpansionId");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Blueprints",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddForeignKey(
                name: "FK_Blueprints_Games_GameId",
                table: "Blueprints",
                column: "GameId",
                principalTable: "Games",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
