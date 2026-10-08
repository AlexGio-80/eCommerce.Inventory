using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPricingRuleFallbackOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinComparableOffers",
                table: "PricingRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnlyCtZeroSellers",
                table: "PricingRules",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinComparableOffers",
                table: "PricingRules");

            migrationBuilder.DropColumn(
                name: "OnlyCtZeroSellers",
                table: "PricingRules");
        }
    }
}
