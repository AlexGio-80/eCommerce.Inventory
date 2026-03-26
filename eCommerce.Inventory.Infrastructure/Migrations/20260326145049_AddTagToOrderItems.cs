using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTagToOrderItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Tag",
                table: "OrderItems",
                type: "nvarchar(max)",
                nullable: true);

            // Backfill: copy existing UserDataField values into the new Tag column
            migrationBuilder.Sql("UPDATE OrderItems SET Tag = UserDataField WHERE Tag IS NULL AND UserDataField IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tag",
                table: "OrderItems");
        }
    }
}
