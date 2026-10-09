using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Secret Lair, Fase 3: stima del valore di un drop dalle stampe esistenti, con la curva del
/// sovrapprezzo misurata sui drop passati, e confronto con il prezzo del negozio.
/// </summary>
public class SecretLairDropValuationTests : IDisposable
{
    // --- Curva del sovrapprezzo ---

    private static IEnumerable<(decimal, decimal)> Samples(decimal basePrice, decimal slPrice, int count) =>
        Enumerable.Repeat((basePrice, slPrice), count);

    [Fact]
    public void Sotto_il_primo_punto_vale_il_pavimento_e_fra_due_punti_si_interpola()
    {
        var curve = SecretLairPremiumCurve.Fit(
            Samples(0.10m, 4m, 20).Concat(Samples(2m, 8m, 20)).Concat(Samples(10m, 20m, 20)),
            new[] { 9m, 11m });

        curve.Points.Should().HaveCount(3);
        curve.Estimate(0.02m).Should().Be(4m, "una carta da pochi centesimi in versione SL vale comunque il pavimento");
        curve.Estimate(1.05m).Should().Be(6m, "a metà strada fra 0,10 e 2 €");
        curve.Estimate(6m).Should().Be(14m);
        curve.Estimate(20m).Should().Be(40m, "oltre l'ultimo punto vale il suo rapporto (×2)");
        curve.NoBasePrice.Should().Be(10m, "mediana delle carte senza altre stampe");
    }

    [Fact]
    public void La_curva_non_scende_quando_due_fasce_vicine_si_invertono()
    {
        var curve = SecretLairPremiumCurve.Fit(
            Samples(1m, 8m, 20).Concat(Samples(2m, 6m, 20)).Concat(Samples(5m, 12m, 20)),
            Array.Empty<decimal>());

        curve.Points.Select(p => p.SecretLairPrice).Should().BeInAscendingOrder();
        curve.Estimate(2m).Should().Be(8m);
    }

    [Fact]
    public void Senza_campioni_la_curva_non_stima()
    {
        SecretLairPremiumCurve.Fit(Array.Empty<(decimal, decimal)>(), Array.Empty<decimal>()).Estimate(3m).Should().BeNull();
    }

    // --- Righe del negozio ---

    [Theory]
    [InlineData("Sol Ring", false, "Sol Ring", false)]
    [InlineData("Foil Sol Ring", false, "Sol Ring", true)]
    [InlineData("Pool Party Foil Lightning Bolt", false, "Lightning Bolt", true)]
    [InlineData("Crackle with Power", true, "Crackle with Power", true)]
    [InlineData("Non-foil reprints", false, "Non-foil reprints", false)]
    public void Il_trattamento_davanti_al_nome_si_toglie_e_rende_la_carta_foil(string line, bool productFoil, string name, bool foil)
    {
        SecretLairDropValuationService.CleanCardLine(line, productFoil).Should().Be((name, foil));
    }

    [Theory]
    [InlineData("1 rare or mythic rare card", true)]
    [InlineData("Non-foil reprints", true)]
    [InlineData("Ultra Pro Stitched Playmat", true)]
    [InlineData("Secret Lair x Stardew Valley: Life in Pelican Town Foil Edition", true)]
    [InlineData("Winota, Joiner of Forces", false)]
    [InlineData("Konda’s Banner", false)]
    public void Le_righe_che_non_sono_carte_non_si_stimano(string line, bool nonCard)
    {
        SecretLairDropValuationService.LooksLikeNonCard(line).Should().Be(nonCard);
    }

    [Fact]
    public void Le_carte_a_due_facce_si_trovano_anche_per_la_faccia_frontale()
    {
        SecretLairDropValuationService.NameKeys("Virtue of Persistence // Locthwain Scorn")
            .Should().BeEquivalentTo(new[] { "virtue of persistence // locthwain scorn", "virtue of persistence" });
        SecretLairDropValuationService.NormalizeCardName("Konda’s  Banner").Should().Be("konda's banner");
    }

    // --- Valutazione sul database ---

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    /// <summary>Costi di vendita al 15%, niente costo per carta: valore realizzabile = prezzo, netto = 85%.</summary>
    private SecretLairDropValuationService Service()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Purchasing:SellingCostPercent"] = "15",
                ["Purchasing:CostPerCard"] = "0",
                ["Purchasing:SecretLair:BuyMarginPercent"] = "30"
            })
            .Build();
        var analysis = new SealedProductAnalysisService(_db, Mock.Of<ICardTraderApiService>(), new BulkSellThroughService(_db),
            new PriceRealizationService(_db), configuration, NullLogger<SealedProductAnalysisService>.Instance);
        return new SecretLairDropValuationService(_db, new SecretLairRetrospectiveService(_db, configuration), analysis, configuration);
    }

    private int _nextCardmarketId = 1000;

    private void Printing(string setCode, string name, decimal trend)
    {
        var id = _nextCardmarketId++;
        _db.MtgjsonCards.Add(new MtgjsonCard { Uuid = Guid.NewGuid(), SetCode = setCode, Name = name, ScryfallId = $"{setCode}-{name}", CardmarketId = id });
        _db.CardmarketLatestPrices.Add(new CardmarketLatestPrice { IdProduct = id, Date = new DateOnly(2026, 10, 9), Trend = trend, TrendFoil = trend });
    }

    /// <summary>Drop passato su MTGJSON, con una carta Secret Lair al prezzo indicato.</summary>
    private void PastDrop(int id, string name, string card, decimal slPrice)
    {
        var uuid = Guid.NewGuid();
        var cmId = _nextCardmarketId++;
        _db.MtgjsonCards.Add(new MtgjsonCard { Uuid = uuid, SetCode = "SLD", Name = card, ScryfallId = $"sld-{card}", CardmarketId = cmId });
        _db.CardmarketLatestPrices.Add(new CardmarketLatestPrice { IdProduct = cmId, Date = new DateOnly(2026, 10, 9), Trend = slPrice });
        var deck = new MtgjsonDeck { SetCode = "SLD", Name = name + " deck" };
        deck.Cards.Add(new MtgjsonDeckCard { CardUuid = uuid, Count = 1, IsFoil = false });
        _db.MtgjsonDecks.Add(deck);
        _db.SealedProducts.Add(new SealedProduct
        {
            Id = id, Uuid = Guid.NewGuid(), SetCode = "SLD", Name = name, Category = "box_set", Subtype = "secret_lair",
            Contents = { new SealedProductContent { Kind = SealedContentKind.Deck, Name = deck.Name, SetCode = "SLD", Count = 1 } }
        });
    }

    private SecretLairShopProduct ShopProduct(string id, string title, decimal price, params (int Quantity, string Line)[] lines)
    {
        var product = new SecretLairShopProduct
        {
            WizardsProductId = id, Title = title, Price = price, ContentsFetchedAt = DateTime.UtcNow,
            Cards = lines.Select(l => new SecretLairShopCard { Quantity = l.Quantity, CardName = l.Line }).ToList()
        };
        _db.SecretLairShopProducts.Add(product);
        return product;
    }

    /// <summary>
    /// Un drop passato: Sol Ring da 2 € vale 10 € in versione SL (curva a un punto, rapporto ×5 oltre).
    /// Arcane Signet costa 4 €, quindi nel drop nuovo vale 20 €.
    /// </summary>
    private async Task SeedAsync()
    {
        Printing("C21", "Sol Ring", 2m);
        Printing("C21", "Arcane Signet", 4m);
        PastDrop(1, "Secret Lair Drop Old Rings", "Sol Ring", 10m);

        ShopProduct("new", "Shiny New Drop", 34.99m, (1, "Sol Ring"), (1, "Arcane Signet"), (1, "1 rare or mythic rare card"));
        ShopProduct("bundle", "Shiny Bundle", 40m, (2, "Shiny New Drop"));
        ShopProduct("old", "Old Rings", 34.99m, (1, "Sol Ring"));
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Il_drop_nuovo_vale_le_stampe_esistenti_col_sovrapprezzo()
    {
        await SeedAsync();

        var valuation = await Service().GetAsync();

        var fresh = valuation.Products.Single(p => p.WizardsProductId == "new");
        fresh.EstimatedTrend.Should().Be(30m, "10 € per Sol Ring e 20 € per Arcane Signet");
        fresh.EstimatedNetValue.Should().Be(25.5m, "al netto del 15% di costi di vendita");
        fresh.RealNetValue.Should().BeNull("il drop non ha ancora prezzi propri");
        fresh.UnknownLines.Should().Be(1, "la carta a sorpresa non si stima");
        fresh.MarginPercent.Should().Be(Math.Round((25.5m - 34.99m) / 34.99m * 100m, 1));
        fresh.Verdict.Should().Be("Lascia");
        fresh.Cards.Single(c => c.Line == "Arcane Signet").Should().Match<SecretLairCardEstimate>(c =>
            c.Kind == SecretLairCardKind.Card && c.BasePrice == 4m && c.EstimatedPrice == 20m);
    }

    [Fact]
    public async Task Un_bundle_vale_i_drop_che_contiene()
    {
        await SeedAsync();

        var bundle = (await Service().GetAsync()).Products.Single(p => p.WizardsProductId == "bundle");

        bundle.EstimatedTrend.Should().Be(60m);
        bundle.EstimatedNetValue.Should().Be(51m);
        bundle.Verdict.Should().Be("Al limite", "51 € contro 40 € è il 27,5%, sotto la soglia del 30%");
    }

    [Fact]
    public async Task Un_drop_uscito_usa_i_prezzi_reali()
    {
        await SeedAsync();

        var old = (await Service().GetAsync()).Products.Single(p => p.WizardsProductId == "old");

        old.RealTrend.Should().Be(10m);
        old.RealNetValue.Should().Be(8.5m);
        old.Cards.Single().RealPrice.Should().Be(10m);
    }

    [Fact]
    public async Task La_stima_si_congela_solo_per_i_drop_senza_prezzi_propri()
    {
        await SeedAsync();

        var frozen = await Service().FreezeEstimatesAsync();

        var products = await _db.SecretLairShopProducts.AsNoTracking().ToDictionaryAsync(p => p.WizardsProductId);
        frozen.Should().Be(2);
        products["new"].EstimatedNetValue.Should().Be(25.5m);
        products["bundle"].EstimatedNetValue.Should().Be(51m);
        products["old"].EstimatedAt.Should().BeNull("ha già i prezzi reali");

        (await Service().FreezeEstimatesAsync()).Should().Be(0, "una stima congelata non si riscrive");
    }

    [Fact]
    public void L_avviso_riporta_valore_margine_e_suggerimento()
    {
        var estimate = new SecretLairDropEstimate("1", 34.99m, "Compra", 60m, 51m, null, null, 45.8m, 0m, 0, null, null, null, new());

        SecretLairDropValuationService.Describe(estimate).Should().Be("Stima: valore netto 51,00 € contro 34,99 € (+46%) → Compra");
    }
}
