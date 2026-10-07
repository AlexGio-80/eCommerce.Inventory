using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSealedProductCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MtgjsonSets",
                columns: table => new
                {
                    Code = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ReleaseDate = table.Column<DateOnly>(type: "date", nullable: true),
                    LastImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MtgjsonSets", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "SealedProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Subtype = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CardCount = table.Column<int>(type: "int", nullable: true),
                    CardmarketId = table.Column<int>(type: "int", nullable: true),
                    CardTraderBlueprintId = table.Column<int>(type: "int", nullable: true),
                    LastImportedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CtMinPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CtOfferCount = table.Column<int>(type: "int", nullable: true),
                    CtPriceUpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SealedProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SealedProductContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SealedProductId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SetCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PackCode = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ChildUuid = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Foil = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SealedProductContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SealedProductContents_SealedProducts_SealedProductId",
                        column: x => x.SealedProductId,
                        principalTable: "SealedProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MtgjsonSet_ParentCode",
                table: "MtgjsonSets",
                column: "ParentCode");

            migrationBuilder.CreateIndex(
                name: "IX_SealedProductContents_SealedProductId",
                table: "SealedProductContents",
                column: "SealedProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SealedProduct_CardmarketId",
                table: "SealedProducts",
                column: "CardmarketId");

            migrationBuilder.CreateIndex(
                name: "IX_SealedProduct_SetCode",
                table: "SealedProducts",
                column: "SetCode");

            migrationBuilder.CreateIndex(
                name: "IX_SealedProduct_Uuid",
                table: "SealedProducts",
                column: "Uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MtgjsonSets");

            migrationBuilder.DropTable(
                name: "SealedProductContents");

            migrationBuilder.DropTable(
                name: "SealedProducts");
        }
    }
}
