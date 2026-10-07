using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlignExpansionRoiMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Nessuna modifica al database: la vista ExpansionsROI resta com'è. Dalla mappatura EF sono
            // state tolte le sue colonne TotaleAcquistato e Differenza, che contavano due volte il costo
            // delle modifiche fatte dalla maschera; il costo ora viene da PurchaseCostService. La
            // migration serve solo ad allineare lo snapshot del modello.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
