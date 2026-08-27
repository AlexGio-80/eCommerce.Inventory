using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSkipWhenFewerOffersThanPosition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SkipWhenFewerOffersThanPosition",
                table: "PricingProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SkipWhenFewerOffersThanPosition",
                table: "PricingProfiles");
        }
    }
}
