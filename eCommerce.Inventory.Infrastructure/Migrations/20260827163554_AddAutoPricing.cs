using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoPricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PricingProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DryRun = table.Column<bool>(type: "bit", nullable: false),
                    MinPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    MaxChangePercentPerRun = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    IncludeProSellers = table.Column<bool>(type: "bit", nullable: false),
                    IncludeNormalSellers = table.Column<bool>(type: "bit", nullable: false),
                    ExcludeVacationSellers = table.Column<bool>(type: "bit", nullable: false),
                    MinSellerDailyCapacity = table.Column<int>(type: "int", nullable: true),
                    CountryCodesCsv = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    EnableOutlierRejection = table.Column<bool>(type: "bit", nullable: false),
                    OutlierMadThreshold = table.Column<decimal>(type: "decimal(9,4)", precision: 9, scale: 4, nullable: false),
                    MinOffersForOutlierRejection = table.Column<int>(type: "int", nullable: false),
                    MinComparableOffers = table.Column<int>(type: "int", nullable: false),
                    MatchCondition = table.Column<bool>(type: "bit", nullable: false),
                    MatchLanguage = table.Column<bool>(type: "bit", nullable: false),
                    MatchFoil = table.Column<bool>(type: "bit", nullable: false),
                    ExcludeSigned = table.Column<bool>(type: "bit", nullable: false),
                    ExcludeAltered = table.Column<bool>(type: "bit", nullable: false),
                    ExcludeGraded = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PricingRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PricingProfileId = table.Column<int>(type: "int", nullable: false),
                    FromPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ToPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReferenceMode = table.Column<int>(type: "int", nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    AdjustmentAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AdjustmentPercent = table.Column<decimal>(type: "decimal(9,2)", precision: 9, scale: 2, nullable: false),
                    CanIncrease = table.Column<bool>(type: "bit", nullable: false),
                    CanDecrease = table.Column<bool>(type: "bit", nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingRules_PricingProfiles_PricingProfileId",
                        column: x => x.PricingProfileId,
                        principalTable: "PricingProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PricingRunLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PricingProfileId = table.Column<int>(type: "int", nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    DryRun = table.Column<bool>(type: "bit", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlannedCount = table.Column<int>(type: "int", nullable: false),
                    EvaluatedCount = table.Column<int>(type: "int", nullable: false),
                    AppliedCount = table.Column<int>(type: "int", nullable: false),
                    SimulatedCount = table.Column<int>(type: "int", nullable: false),
                    NoChangeCount = table.Column<int>(type: "int", nullable: false),
                    SkippedCount = table.Column<int>(type: "int", nullable: false),
                    FailedCount = table.Column<int>(type: "int", nullable: false),
                    TotalPriceDelta = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingRunLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PricingRunLogs_PricingProfiles_PricingProfileId",
                        column: x => x.PricingProfileId,
                        principalTable: "PricingProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InventoryItemId = table.Column<int>(type: "int", nullable: false),
                    BlueprintId = table.Column<int>(type: "int", nullable: false),
                    PricingRunLogId = table.Column<int>(type: "int", nullable: true),
                    PricingRuleId = table.Column<int>(type: "int", nullable: true),
                    OldPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProposedPrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    ReferencePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    ComparableOffersCount = table.Column<int>(type: "int", nullable: false),
                    OutliersRejectedCount = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceChangeLogs_Blueprints_BlueprintId",
                        column: x => x.BlueprintId,
                        principalTable: "Blueprints",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PriceChangeLogs_InventoryItems_InventoryItemId",
                        column: x => x.InventoryItemId,
                        principalTable: "InventoryItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceChangeLogs_PricingRunLogs_PricingRunLogId",
                        column: x => x.PricingRunLogId,
                        principalTable: "PricingRunLogs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeLog_Blueprint_CreatedAt",
                table: "PriceChangeLogs",
                columns: new[] { "BlueprintId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeLog_CreatedAt",
                table: "PriceChangeLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeLogs_InventoryItemId",
                table: "PriceChangeLogs",
                column: "InventoryItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PriceChangeLogs_PricingRunLogId",
                table: "PriceChangeLogs",
                column: "PricingRunLogId");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRule_Profile_Range",
                table: "PricingRules",
                columns: new[] { "PricingProfileId", "FromPrice", "ToPrice" });

            migrationBuilder.CreateIndex(
                name: "IX_PricingRunLog_StartedAt",
                table: "PricingRunLogs",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PricingRunLogs_PricingProfileId",
                table: "PricingRunLogs",
                column: "PricingProfileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceChangeLogs");

            migrationBuilder.DropTable(
                name: "PricingRules");

            migrationBuilder.DropTable(
                name: "PricingRunLogs");

            migrationBuilder.DropTable(
                name: "PricingProfiles");
        }
    }
}
