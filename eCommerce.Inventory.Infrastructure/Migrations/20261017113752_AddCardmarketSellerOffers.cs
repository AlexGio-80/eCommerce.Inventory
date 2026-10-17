using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCardmarketSellerOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardmarketSellerOffers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Seller = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdProduct = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsDamaged = table.Column<bool>(type: "bit", nullable: false),
                    PastedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardmarketSellerOffers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardmarketSellerOffers_PastedAt",
                table: "CardmarketSellerOffers",
                column: "PastedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CardmarketSellerOffers_Seller_IdProduct",
                table: "CardmarketSellerOffers",
                columns: new[] { "Seller", "IdProduct" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardmarketSellerOffers");
        }
    }
}
