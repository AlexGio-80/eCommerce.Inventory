using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertRuleScopes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RecentReleaseDays",
                table: "AlertRules",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Subtype",
                table: "AlertRules",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            // Regole predefinite (create una volta sola: se l'utente le cancella non ricompaiono).
            // Type: 1 = calo di prezzo, 2 = apertura conveniente, 3 = prezzo al minimo.
            migrationBuilder.Sql(@"
INSERT INTO AlertRules (Name, Type, SealedProductId, SetCode, Category, Subtype, RecentReleaseDays, Threshold, UseLowPrice, IsActive, SendEmail, CreatedAt)
VALUES
 (N'Box da aprire (resa almeno 15%)', 2, NULL, NULL, N'booster_box', NULL, NULL, 15, 0, 1, 1, SYSUTCDATETIME()),
 (N'Box delle uscite recenti in calo (almeno 10% in 7 giorni)', 1, NULL, NULL, N'booster_box', NULL, 180, 10, 0, 1, 1, SYSUTCDATETIME()),
 (N'Box delle uscite recenti al minimo di 90 giorni', 3, NULL, NULL, N'booster_box', NULL, 365, 90, 0, 1, 1, SYSUTCDATETIME());");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM AlertRules WHERE Name IN (
 N'Box da aprire (resa almeno 15%)',
 N'Box delle uscite recenti in calo (almeno 10% in 7 giorni)',
 N'Box delle uscite recenti al minimo di 90 giorni');");

            migrationBuilder.DropColumn(
                name: "RecentReleaseDays",
                table: "AlertRules");

            migrationBuilder.DropColumn(
                name: "Subtype",
                table: "AlertRules");
        }
    }
}
