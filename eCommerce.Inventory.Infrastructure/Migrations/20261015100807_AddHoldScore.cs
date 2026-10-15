using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHoldScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FloorPercent",
                table: "SealedOpportunities",
                type: "decimal(9,1)",
                precision: 9,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HistoryPosition",
                table: "SealedOpportunities",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HoldScore",
                table: "SealedOpportunities",
                type: "decimal(5,1)",
                precision: 5,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHoldCandidate",
                table: "SealedOpportunities",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ReleaseAgeMonths",
                table: "SealedOpportunities",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FloorPercent",
                table: "SealedOpportunities");

            migrationBuilder.DropColumn(
                name: "HistoryPosition",
                table: "SealedOpportunities");

            migrationBuilder.DropColumn(
                name: "HoldScore",
                table: "SealedOpportunities");

            migrationBuilder.DropColumn(
                name: "IsHoldCandidate",
                table: "SealedOpportunities");

            migrationBuilder.DropColumn(
                name: "ReleaseAgeMonths",
                table: "SealedOpportunities");
        }
    }
}
