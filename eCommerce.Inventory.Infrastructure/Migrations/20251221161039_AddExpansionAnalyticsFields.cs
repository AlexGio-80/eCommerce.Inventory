using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpansionAnalyticsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AverageCardValue",
                table: "Expansions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastValueAnalysisUpdate",
                table: "Expansions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalMinPrice",
                table: "Expansions",
                type: "decimal(18,2)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AverageCardValue",
                table: "Expansions");

            migrationBuilder.DropColumn(
                name: "LastValueAnalysisUpdate",
                table: "Expansions");

            migrationBuilder.DropColumn(
                name: "TotalMinPrice",
                table: "Expansions");
        }
    }
}
