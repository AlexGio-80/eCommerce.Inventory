using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRarityStatsToExpansion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AvgValueCommon",
                table: "Expansions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AvgValueMythic",
                table: "Expansions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AvgValueRare",
                table: "Expansions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AvgValueUncommon",
                table: "Expansions",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvgValueCommon",
                table: "Expansions");

            migrationBuilder.DropColumn(
                name: "AvgValueMythic",
                table: "Expansions");

            migrationBuilder.DropColumn(
                name: "AvgValueRare",
                table: "Expansions");

            migrationBuilder.DropColumn(
                name: "AvgValueUncommon",
                table: "Expansions");
        }
    }
}
