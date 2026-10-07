using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOpeningValueData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DetailImportedAt",
                table: "MtgjsonSets",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasBoosterData",
                table: "MtgjsonSets",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "BoosterConfigs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SetCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BoosterType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Weight = table.Column<int>(type: "int", nullable: false),
                    TotalWeight = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoosterConfigs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoosterSheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SetCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    BoosterType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsFoil = table.Column<bool>(type: "bit", nullable: false),
                    TotalWeight = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoosterSheets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CardmarketLatestPrices",
                columns: table => new
                {
                    IdProduct = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Trend = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Low = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TrendFoil = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LowFoil = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardmarketLatestPrices", x => x.IdProduct);
                });

            migrationBuilder.CreateTable(
                name: "CardTraderCardPrices",
                columns: table => new
                {
                    BlueprintId = table.Column<int>(type: "int", nullable: false),
                    IsFoil = table.Column<bool>(type: "bit", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    OfferCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardTraderCardPrices", x => new { x.BlueprintId, x.IsFoil });
                });

            migrationBuilder.CreateTable(
                name: "MtgjsonCards",
                columns: table => new
                {
                    Uuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SetCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Number = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Rarity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CardmarketId = table.Column<int>(type: "int", nullable: true),
                    ScryfallId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MtgjsonCards", x => x.Uuid);
                });

            migrationBuilder.CreateTable(
                name: "MtgjsonDecks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SetCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MtgjsonDecks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BoosterConfigSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BoosterConfigId = table.Column<int>(type: "int", nullable: false),
                    SheetName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoosterConfigSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoosterConfigSlots_BoosterConfigs_BoosterConfigId",
                        column: x => x.BoosterConfigId,
                        principalTable: "BoosterConfigs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BoosterSheetCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BoosterSheetId = table.Column<int>(type: "int", nullable: false),
                    CardUuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Weight = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoosterSheetCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BoosterSheetCards_BoosterSheets_BoosterSheetId",
                        column: x => x.BoosterSheetId,
                        principalTable: "BoosterSheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MtgjsonDeckCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MtgjsonDeckId = table.Column<int>(type: "int", nullable: false),
                    CardUuid = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    IsFoil = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MtgjsonDeckCards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MtgjsonDeckCards_MtgjsonDecks_MtgjsonDeckId",
                        column: x => x.MtgjsonDeckId,
                        principalTable: "MtgjsonDecks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoosterConfig_Set_Type",
                table: "BoosterConfigs",
                columns: new[] { "SetCode", "BoosterType" });

            migrationBuilder.CreateIndex(
                name: "IX_BoosterConfigSlots_BoosterConfigId",
                table: "BoosterConfigSlots",
                column: "BoosterConfigId");

            migrationBuilder.CreateIndex(
                name: "IX_BoosterSheetCards_BoosterSheetId",
                table: "BoosterSheetCards",
                column: "BoosterSheetId");

            migrationBuilder.CreateIndex(
                name: "IX_BoosterSheet_Set_Type",
                table: "BoosterSheets",
                columns: new[] { "SetCode", "BoosterType" });

            migrationBuilder.CreateIndex(
                name: "IX_MtgjsonCard_SetCode",
                table: "MtgjsonCards",
                column: "SetCode");

            migrationBuilder.CreateIndex(
                name: "IX_MtgjsonDeckCards_MtgjsonDeckId",
                table: "MtgjsonDeckCards",
                column: "MtgjsonDeckId");

            migrationBuilder.CreateIndex(
                name: "IX_MtgjsonDeck_SetCode",
                table: "MtgjsonDecks",
                column: "SetCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoosterConfigSlots");

            migrationBuilder.DropTable(
                name: "BoosterSheetCards");

            migrationBuilder.DropTable(
                name: "CardmarketLatestPrices");

            migrationBuilder.DropTable(
                name: "CardTraderCardPrices");

            migrationBuilder.DropTable(
                name: "MtgjsonCards");

            migrationBuilder.DropTable(
                name: "MtgjsonDeckCards");

            migrationBuilder.DropTable(
                name: "BoosterConfigs");

            migrationBuilder.DropTable(
                name: "BoosterSheets");

            migrationBuilder.DropTable(
                name: "MtgjsonDecks");

            migrationBuilder.DropColumn(
                name: "DetailImportedAt",
                table: "MtgjsonSets");

            migrationBuilder.DropColumn(
                name: "HasBoosterData",
                table: "MtgjsonSets");
        }
    }
}
