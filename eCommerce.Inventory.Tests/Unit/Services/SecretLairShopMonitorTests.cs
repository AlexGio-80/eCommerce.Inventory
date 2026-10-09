using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.SecretLair;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using eCommerce.Inventory.Infrastructure.BackgroundJobs;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Negozio Secret Lair: lettura del catalogo (JSON di StoreSearch) e delle carte (HTML della pagina
/// prodotto), e aggiornamento dei prodotti con gli avvisi per i drop nuovi.
/// </summary>
public class SecretLairShopMonitorTests : IDisposable
{
    /// <summary>Estratto reale della risposta di StoreSearch (08/10/2026), ridotto ai campi usati.</summary>
    private const string CatalogJson = """
    {"filters":[{"count":2,"total":2,"products":[
      {"productID":"1246223","release_date":"2026-09-30T09:00:00.000+02:00","refs":{"refID":"D51280000-EU"},
       "price_info":{"prices":[{"licence":"EUR","price":"34.99"}]},
       "descriptions":[{"lang":"FR","title":"Les Oddlands"},{"lang":"EN","title":"The Oddlands: Plains"}],
       "stock":{"stock":10,"ostock":"DENY_PURCHASE"},
       "categories":[{"categoryID":"92094","categoryName":"bu-SL. Chaos Vault: The Oddlands"}],
       "specific":{"extension":{"videogame":{"edition":"NON-FOIL"}}},
       "preorder":"0","limit_purchase":"2",
       "flash_sales":{"start_date":"2026-09-28 18:00:00","end_date":"2050-12-31 09:00:00"},
       "prices":[{"currency":"EUR","price":"34.99"}]},
      {"productID":"1254741","release_date":"2026-11-03T09:00:00.000+01:00",
       "descriptions":[{"lang":"EN","title":"Secret Lair x Masters of the Universe Foil Edition"}],
       "stock":{"stock":0},
       "categories":[{"categoryName":"bu-SL. The Most Powerful Superdrop in the Universe"}],
       "specific":{"extension":{"videogame":{"edition":"FOIL"}}},
       "preorder":"1","limit_purchase":"2","prices":[{"currency":"EUR","price":"44.99"}]}
    ]}]}
    """;

    private const string ProductHtml = """
    <html><body><div id="collapseLong" class="panel-collapse"><div class="force-overflow">
    <div class="product-information"><ul>
      <li>1x Winota, Joiner of Forces as &ldquo;He-Man, Champion of Eternia&rdquo;</li>
      <li>1x Crackle with Power</li>
      <li>2x Konda&rsquo;s Banner as "He-Man&rsquo;s Battle Harness"</li>
    </ul><br>Available while supplies last.</div></div></div></body></html>
    """;

    [Fact]
    public void Il_catalogo_si_legge_dal_json_di_StoreSearch()
    {
        var (items, total) = SecretLairShopClient.ParseCatalog(CatalogJson);

        total.Should().Be(2);
        var plains = items[0];
        plains.ProductId.Should().Be("1246223");
        plains.Title.Should().Be("The Oddlands: Plains", "il titolo inglese");
        plains.DropName.Should().Be("Chaos Vault: The Oddlands");
        plains.IsFoil.Should().BeFalse();
        plains.Price.Should().Be(34.99m);
        plains.Stock.Should().Be(10);
        plains.LimitPerCustomer.Should().Be(2);
        plains.SaleStart.Should().Be(new DateTime(2026, 9, 28, 18, 0, 0));
        plains.SaleEnd.Should().BeNull("il 2050 vuol dire nessuna fine");

        items[1].IsFoil.Should().BeTrue();
        items[1].IsPreorder.Should().BeTrue();
        items[1].Price.Should().Be(44.99m);
    }

    [Fact]
    public void Una_risposta_con_forma_diversa_e_un_errore_non_un_catalogo_vuoto()
    {
        var parse = () => SecretLairShopClient.ParseCatalog("""{"results":[]}""");
        parse.Should().Throw<FormatException>();
    }

    [Fact]
    public void Le_carte_si_leggono_dalla_pagina_del_prodotto()
    {
        var cards = SecretLairShopClient.ParseContents(ProductHtml);

        cards.Should().HaveCount(3);
        cards[0].Should().Be(new SecretLairShopCardLine(1, "Winota, Joiner of Forces", "He-Man, Champion of Eternia"));
        cards[1].Should().Be(new SecretLairShopCardLine(1, "Crackle with Power", null));
        cards[2].Quantity.Should().Be(2);
        cards[2].CardName.Should().Be("Konda’s Banner");
        cards[2].DisplayName.Should().Be("He-Man’s Battle Harness");
    }

    [Fact]
    public void Il_prossimo_giro_e_il_primo_orario_dopo_adesso()
    {
        var times = new[] { new TimeSpan(8, 0, 0), new TimeSpan(14, 0, 0), new TimeSpan(20, 0, 0) };

        SecretLairMonitorWorker.NextRunTime(new DateTime(2026, 10, 8, 9, 30, 0), times).Should().Be(new DateTime(2026, 10, 8, 14, 0, 0));
        SecretLairMonitorWorker.NextRunTime(new DateTime(2026, 10, 8, 21, 0, 0), times).Should().Be(new DateTime(2026, 10, 9, 8, 0, 0));
    }

    // --- Monitoraggio ---

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly Mock<ISecretLairShopClient> _client = new();

    public void Dispose() => _db.Dispose();

    private SecretLairShopMonitorService Service(ISecretLairDropValuation? valuation = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["SecretLair:Monitor:DelaySeconds"] = "0" })
            .Build();
        var email = new Mock<IEmailSender>();
        email.SetupGet(e => e.IsConfigured).Returns(false);
        var alerts = new AlertService(_db, email.Object, NullLogger<AlertService>.Instance);
        return new SecretLairShopMonitorService(_db, _client.Object, alerts, configuration, NullLogger<SecretLairShopMonitorService>.Instance, valuation);
    }

    private static SecretLairShopItem Item(string id, string drop, int? stock = 10, bool foil = false) =>
        new(id, null, $"Prodotto {id}", drop, foil, foil ? 44.99m : 34.99m, stock, false, 2, null, null, null);

    private void Catalog(params SecretLairShopItem[] items) =>
        _client.Setup(c => c.GetCatalogAsync(It.IsAny<CancellationToken>())).ReturnsAsync(items.ToList());

    [Fact]
    public async Task La_prima_lettura_registra_il_catalogo_senza_avvisi()
    {
        Catalog(Item("1", "Drop A"), Item("2", "Drop B"));
        _client.Setup(c => c.GetContentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecretLairShopCardLine> { new(1, "Sol Ring", null) });

        var run = await Service().RunAsync();

        run.Outcome.Should().Be(SecretLairShopRunOutcome.Succeeded);
        run.NewProducts.Should().Be(2);
        run.ContentsFetched.Should().Be(2);
        (await _db.SecretLairShopProducts.CountAsync()).Should().Be(2);
        (await _db.SecretLairShopCards.CountAsync()).Should().Be(2);
        (await _db.AlertNotifications.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Un_drop_nuovo_genera_un_avviso_con_le_sue_versioni()
    {
        Catalog(Item("1", "Drop A"));
        _client.Setup(c => c.GetContentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecretLairShopCardLine> { new(1, "Sol Ring", "Anello") });
        await Service().RunAsync();

        Catalog(Item("1", "Drop A"), Item("2", "Superdrop B"), Item("3", "Superdrop B", foil: true));
        await Service().RunAsync();

        var notification = (await _db.AlertNotifications.ToListAsync()).Should().ContainSingle().Subject;
        notification.Title.Should().Be("Nuovo drop Secret Lair: Superdrop B");
        notification.Message.Should().Contain("Prodotto 2").And.Contain("Prodotto 3").And.Contain("Carte: Sol Ring");
    }

    [Fact]
    public async Task L_avviso_di_un_drop_nuovo_riporta_la_stima_e_la_lettura_congela_le_stime()
    {
        Catalog(Item("1", "Drop A"));
        _client.Setup(c => c.GetContentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SecretLairShopCardLine> { new(1, "Sol Ring", null) });
        var valuation = new Mock<ISecretLairDropValuation>();
        valuation.Setup(v => v.EvaluateProductsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyCollection<int> ids, CancellationToken _) => ids.ToDictionary(id => id, _ =>
                new SecretLairDropEstimate("x", 34.99m, "Compra", 60m, 51m, null, null, 45.8m, 0m, 0, null, null, null, new())));
        await Service(valuation.Object).RunAsync();

        Catalog(Item("1", "Drop A"), Item("2", "Superdrop B"));
        await Service(valuation.Object).RunAsync();

        var notification = (await _db.AlertNotifications.ToListAsync()).Should().ContainSingle().Subject;
        notification.Message.Should().Contain("Stima: valore netto 51,00 € contro 34,99 € (+46%) → Compra");
        valuation.Verify(v => v.FreezeEstimatesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Se_la_stima_non_riesce_l_avviso_parte_lo_stesso()
    {
        Catalog(Item("1", "Drop A"));
        _client.Setup(c => c.GetContentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SecretLairShopCardLine>());
        var valuation = new Mock<ISecretLairDropValuation>();
        valuation.Setup(v => v.FreezeEstimatesAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("boom"));
        valuation.Setup(v => v.EvaluateProductsAsync(It.IsAny<IReadOnlyCollection<int>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        await Service(valuation.Object).RunAsync();

        Catalog(Item("1", "Drop A"), Item("2", "Superdrop B"));
        var run = await Service(valuation.Object).RunAsync();

        run.Outcome.Should().Be(SecretLairShopRunOutcome.Succeeded);
        (await _db.AlertNotifications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Esauriti_e_tolti_dal_negozio_restano_con_la_data()
    {
        Catalog(Item("1", "Drop A"), Item("2", "Drop B"));
        _client.Setup(c => c.GetContentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<SecretLairShopCardLine>());
        await Service().RunAsync();

        Catalog(Item("1", "Drop A", stock: 0));
        await Service().RunAsync();

        var products = await _db.SecretLairShopProducts.ToDictionaryAsync(p => p.WizardsProductId);
        products["1"].SoldOutAt.Should().NotBeNull();
        products["1"].RemovedAt.Should().BeNull();
        products["2"].RemovedAt.Should().NotBeNull();
        SecretLairShopMonitorService.StatusOf(products["1"]).Should().Be("Esaurito");
        SecretLairShopMonitorService.StatusOf(products["2"]).Should().Be("Tolto dal negozio");
    }

    [Fact]
    public async Task Se_il_negozio_non_risponde_la_lettura_risulta_fallita()
    {
        _client.Setup(c => c.GetCatalogAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("503"));

        var run = await Service().RunAsync();

        run.Outcome.Should().Be(SecretLairShopRunOutcome.Failed);
        run.Message.Should().Contain("503");
        (await _db.SecretLairShopRuns.SingleAsync()).Outcome.Should().Be(SecretLairShopRunOutcome.Failed);
    }
}
