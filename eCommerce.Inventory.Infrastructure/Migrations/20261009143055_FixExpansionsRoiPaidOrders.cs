using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <summary>
    /// La vista <c>ExpansionsROI</c> (creata a mano nel database, mai in una migration) sommava il
    /// venduto di tutte le righe d'ordine. Gli ordini "hub_pending" di Card Trader Zero sono i singoli
    /// acquisti che Card Trader raccoglie poi in un ordine "Ct connect" pagato: le loro righe sono un
    /// doppione, e per le uscite recenti il venduto risultava esattamente il doppio (17.397 € in più al
    /// 09/10/2026). Ora conta solo gli ordini pagati, come gli altri report. <c>CREATE OR ALTER</c>
    /// perché in un database nuovo la vista non esiste.
    /// </summary>
    public partial class FixExpansionsRoiPaidOrders : Migration
    {
        private const string Acquistato = """
            Acquistato AS (
                -- Calcola il totale dei costi (PendingListings) per Espansione
                -- Dobbiamo trovare i blueprint associati all'espansione senza duplicare i costi per ogni ordine
                SELECT
                    e.Name,
                    SUM(ISNULL(pl.PurchasePrice, 0) * isnull(pl.Quantity, 0)) AS TotaleCosti
                FROM
                    PendingListings pl
                INNER JOIN
                    Blueprints b ON pl.BlueprintId = b.Id
                    inner join Expansions e on e.Id = b.ExpansionId
                GROUP BY
                    e.Name
            )
            SELECT
                v.ExpansionName,
                (v.TotaleVenduto - ISNULL(a.TotaleCosti, 0)) AS Differenza, -- Guadagno/Perdita
                v.TotaleVenduto,
                ISNULL(a.TotaleCosti, 0) AS TotaleAcquistato
            FROM
                Venduto v
            LEFT JOIN
                Acquistato a ON v.ExpansionName = a.Name
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                CREATE OR ALTER VIEW dbo.ExpansionsROI AS
                WITH Venduto AS (
                    -- Totale venduto per espansione, solo ordini pagati: gli "hub_pending" di Card Trader
                    -- Zero sono doppioni delle righe dell'ordine "Ct connect" che li raccoglie
                    SELECT
                        oi.ExpansionName,
                        SUM(ISNULL(oi.Quantity, 0) * ISNULL(oi.Price, 0)) AS TotaleVenduto
                    FROM
                        OrderItems oi
                    INNER JOIN
                        Orders o ON o.Id = oi.OrderId
                    WHERE
                        o.PaidAt IS NOT NULL
                    GROUP BY
                        oi.ExpansionName
                ),
                {Acquistato}
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                CREATE OR ALTER VIEW dbo.ExpansionsROI AS
                WITH Venduto AS (
                    SELECT
                        oi.ExpansionName,
                        SUM(ISNULL(oi.Quantity, 0) * ISNULL(oi.Price, 0)) AS TotaleVenduto
                    FROM
                        OrderItems oi
                    GROUP BY
                        oi.ExpansionName
                ),
                {Acquistato}
                """);
        }
    }
}
