using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingListingGradingNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GradingNotes",
                table: "PendingListings",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GradingNotes",
                table: "PendingListings");
        }
    }
}
