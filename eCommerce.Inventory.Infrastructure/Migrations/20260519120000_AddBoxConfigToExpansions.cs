using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBoxConfigToExpansions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PacksPerBox",
                table: "Expansions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CardsPerPack",
                table: "Expansions",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PacksPerBox",
                table: "Expansions");

            migrationBuilder.DropColumn(
                name: "CardsPerPack",
                table: "Expansions");
        }
    }
}
