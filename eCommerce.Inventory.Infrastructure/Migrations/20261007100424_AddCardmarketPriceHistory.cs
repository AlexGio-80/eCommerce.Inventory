using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCardmarketPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardmarketImportLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SourceCreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SourceLastModified = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SealedProducts = table.Column<int>(type: "int", nullable: false),
                    SealedSnapshotsWritten = table.Column<int>(type: "int", nullable: false),
                    SinglesTracked = table.Column<int>(type: "int", nullable: false),
                    SinglesSnapshotsWritten = table.Column<int>(type: "int", nullable: false),
                    NewProducts = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardmarketImportLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardmarketPriceSnapshots",
                columns: table => new
                {
                    IdProduct = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Avg = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Low = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Trend = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Avg1 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Avg7 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Avg30 = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    AvgFoil = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LowFoil = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TrendFoil = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardmarketPriceSnapshots", x => new { x.IdProduct, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "CardmarketProducts",
                columns: table => new
                {
                    IdProduct = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    IdCategory = table.Column<int>(type: "int", nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IdExpansion = table.Column<int>(type: "int", nullable: false),
                    DateAdded = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsSingle = table.Column<bool>(type: "bit", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardmarketProducts", x => x.IdProduct);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardmarketImportLog_StartedAt",
                table: "CardmarketImportLogs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CardmarketPriceSnapshot_Date",
                table: "CardmarketPriceSnapshots",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_CardmarketProduct_IdExpansion",
                table: "CardmarketProducts",
                column: "IdExpansion");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardmarketImportLogs");

            migrationBuilder.DropTable(
                name: "CardmarketPriceSnapshots");

            migrationBuilder.DropTable(
                name: "CardmarketProducts");
        }
    }
}
