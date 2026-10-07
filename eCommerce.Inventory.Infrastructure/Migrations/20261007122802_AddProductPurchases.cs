using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SealedProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Store = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Seller = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PurchasedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    OpenedAt = table.Column<DateOnly>(type: "date", nullable: true),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    PredictedOpenValueNet = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    PredictionCoverage = table.Column<decimal>(type: "decimal(5,1)", precision: 5, scale: 1, nullable: true),
                    PredictedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPurchases_SealedProducts_SealedProductId",
                        column: x => x.SealedProductId,
                        principalTable: "SealedProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPurchase_Tag",
                table: "ProductPurchases",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPurchases_SealedProductId",
                table: "ProductPurchases",
                column: "SealedProductId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPurchases");
        }
    }
}
