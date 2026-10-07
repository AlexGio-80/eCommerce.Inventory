using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSealedOpportunities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SealedOpportunities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    SealedProductId = table.Column<int>(type: "int", nullable: false),
                    MainSetCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CmTrend = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CmLow = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OpenValueCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CoverageCm = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    SealedNetCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    OpeningRoiPercent = table.Column<decimal>(type: "decimal(9,1)", precision: 9, scale: 1, nullable: true),
                    Decision = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ComputedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SealedOpportunities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SealedOpportunities_SealedProducts_SealedProductId",
                        column: x => x.SealedProductId,
                        principalTable: "SealedProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SealedOpportunity_Date",
                table: "SealedOpportunities",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_SealedOpportunity_Product_Date",
                table: "SealedOpportunities",
                columns: new[] { "SealedProductId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SealedOpportunities");
        }
    }
}
