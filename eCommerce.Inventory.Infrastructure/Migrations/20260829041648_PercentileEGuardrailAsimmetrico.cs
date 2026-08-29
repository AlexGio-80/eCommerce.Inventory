using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace eCommerce.Inventory.Infrastructure.Migrations
{
    /// <summary>
    /// Passa le regole dalla posizione fissa alla collocazione percentuale e separa il guardrail
    /// nelle due direzioni. Le colonne nuove vanno valorizzate esplicitamente sulle righe che
    /// esistono già: i default definiti in C# valgono solo per le entità create dal codice, e un
    /// profilo a database resterebbe con zero, che per i guardrail significa "nessun limite".
    /// </summary>
    public partial class PercentileEGuardrailAsimmetrico : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // La vecchia soglia unica non ha un equivalente nel nuovo schema: il suo compito di
            // difesa dai prezzi anomali passa ai filtri sulle offerte, quello di limite alle
            // variazioni si sdoppia per direzione. Va rimossa, non riusata.
            migrationBuilder.DropColumn(
                name: "MaxChangePercentPerRun",
                table: "PricingProfiles");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxIncreasePercentPerRun",
                table: "PricingProfiles",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 300m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxDecreasePercentPerRun",
                table: "PricingProfiles",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 25m);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxMedianRatio",
                table: "PricingProfiles",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 4m);

            migrationBuilder.AddColumn<decimal>(
                name: "Percentile",
                table: "PricingRules",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 30m);

            // Lo scarto statistico deve poter girare anche sui mercati sottili: è lì che un
            // singolo prezzo di comodo arriva dritto al riferimento.
            migrationBuilder.Sql(
                "UPDATE PricingProfiles SET MinOffersForOutlierRejection = 3 WHERE MinOffersForOutlierRejection > 3;");

            // Conversione delle regole esistenti da posizione fissa (ReferenceMode 0) a
            // collocazione percentuale (ReferenceMode 5). Le percentuali sono un punto di
            // partenza da tarare sull'anteprima: con le offerte comparabili che vanno da 3 a 29
            // non esiste una conversione esatta dall'ordinale.
            migrationBuilder.Sql(@"
UPDATE PricingRules SET ReferenceMode = 5, Percentile = 15 WHERE ReferenceMode = 0 AND FromPrice < 1.01;
UPDATE PricingRules SET ReferenceMode = 5, Percentile = 20 WHERE ReferenceMode = 0 AND FromPrice >= 1.01 AND FromPrice < 25.01;
UPDATE PricingRules SET ReferenceMode = 5, Percentile = 40 WHERE ReferenceMode = 0 AND FromPrice >= 25.01;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE PricingRules SET ReferenceMode = 0 WHERE ReferenceMode = 5;");

            migrationBuilder.DropColumn(name: "Percentile", table: "PricingRules");
            migrationBuilder.DropColumn(name: "MaxMedianRatio", table: "PricingProfiles");
            migrationBuilder.DropColumn(name: "MaxDecreasePercentPerRun", table: "PricingProfiles");
            migrationBuilder.DropColumn(name: "MaxIncreasePercentPerRun", table: "PricingProfiles");

            migrationBuilder.AddColumn<decimal>(
                name: "MaxChangePercentPerRun",
                table: "PricingProfiles",
                type: "decimal(9,2)",
                precision: 9,
                scale: 2,
                nullable: false,
                defaultValue: 50m);
        }
    }
}
