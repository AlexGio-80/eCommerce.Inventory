using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGradingFieldsToPendingListing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "GradingCentering",
                table: "PendingListings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GradingConditionCode",
                table: "PendingListings",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GradingConfidence",
                table: "PendingListings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GradingCorners",
                table: "PendingListings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GradingEdges",
                table: "PendingListings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradingImagesCount",
                table: "PendingListings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GradingScore",
                table: "PendingListings",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GradingSurface",
                table: "PendingListings",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GradingCentering",
                table: "PendingListings");

            migrationBuilder.DropColumn(
                name: "GradingConditionCode",
                table: "PendingListings");

            migrationBuilder.DropColumn(
                name: "GradingConfidence",
                table: "PendingListings");

            migrationBuilder.DropColumn(
                name: "GradingCorners",
                table: "PendingListings");

            migrationBuilder.DropColumn(
                name: "GradingEdges",
                table: "PendingListings");

            migrationBuilder.DropColumn(
                name: "GradingImagesCount",
                table: "PendingListings");

            migrationBuilder.DropColumn(
                name: "GradingScore",
                table: "PendingListings");

            migrationBuilder.DropColumn(
                name: "GradingSurface",
                table: "PendingListings");
        }
    }
}
