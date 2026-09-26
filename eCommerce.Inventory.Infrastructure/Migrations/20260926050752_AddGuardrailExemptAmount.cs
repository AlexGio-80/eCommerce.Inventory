using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGuardrailExemptAmount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default 0,10 e non lo 0 generato da EF: il profilo gia' a database deve ricevere la
            // soglia, altrimenti la migration la aggiungerebbe disattivata.
            migrationBuilder.AddColumn<decimal>(
                name: "GuardrailExemptAmount",
                table: "PricingProfiles",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 0.10m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GuardrailExemptAmount",
                table: "PricingProfiles");
        }
    }
}
