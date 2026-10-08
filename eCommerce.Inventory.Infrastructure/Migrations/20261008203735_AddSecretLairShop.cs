using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSecretLairShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SecretLairShopProducts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WizardsProductId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RefId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DropName = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsFoil = table.Column<bool>(type: "bit", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    Stock = table.Column<int>(type: "int", nullable: true),
                    IsPreorder = table.Column<bool>(type: "bit", nullable: false),
                    LimitPerCustomer = table.Column<int>(type: "int", nullable: true),
                    ReleaseDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SaleStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SaleEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SoldOutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ContentsFetchedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretLairShopProducts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretLairShopRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    Products = table.Column<int>(type: "int", nullable: false),
                    NewProducts = table.Column<int>(type: "int", nullable: false),
                    ContentsFetched = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretLairShopRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SecretLairShopCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SecretLairShopProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CardName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SecretLairShopCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SecretLairShopCards_SecretLairShopProducts_SecretLairShopProductId",
                        column: x => x.SecretLairShopProductId,
                        principalTable: "SecretLairShopProducts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SecretLairShopCards_SecretLairShopProductId",
                table: "SecretLairShopCards",
                column: "SecretLairShopProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SecretLairShopProducts_WizardsProductId",
                table: "SecretLairShopProducts",
                column: "WizardsProductId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SecretLairShopRuns_StartedAt",
                table: "SecretLairShopRuns",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SecretLairShopCards");

            migrationBuilder.DropTable(
                name: "SecretLairShopRuns");

            migrationBuilder.DropTable(
                name: "SecretLairShopProducts");
        }
    }
}
