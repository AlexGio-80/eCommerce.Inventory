using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Retrospettiva dei drop Secret Lair: le carte vendute si riconducono al drop tramite MTGJSON, e
/// conta come drop comprato solo quello con tutte le carte caricate (il resto sono singole sciolte).
/// </summary>
public class SecretLairRetrospectiveServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private int _nextId = 100;

    public void Dispose() => _db.Dispose();

    private SecretLairRetrospectiveService Service() => new(_db, new ConfigurationBuilder().Build());

    /// <summary>Drop con le sue carte su MTGJSON, come mazzo; restituisce gli id Scryfall delle carte.</summary>
    private List<string> AddDrop(int id, string name, int cards, bool foil = false)
    {
        var deck = new MtgjsonDeck { SetCode = "SLD", Name = name + " deck" };
        var scryfall = new List<string>();
        for (var i = 0; i < cards; i++)
        {
            var uuid = Guid.NewGuid();
            var sf = $"{name}-{i}";
            scryfall.Add(sf);
            _db.MtgjsonCards.Add(new MtgjsonCard { Uuid = uuid, SetCode = "SLD", Name = sf, ScryfallId = sf });
            deck.Cards.Add(new MtgjsonDeckCard { CardUuid = uuid, Count = 1, IsFoil = foil });
        }
        _db.MtgjsonDecks.Add(deck);
        _db.SealedProducts.Add(new SealedProduct
        {
            Id = id, Uuid = Guid.NewGuid(), SetCode = "SLD", Name = name, Category = "box_set", Subtype = "secret_lair",
            Contents = { new SealedProductContent { Kind = SealedContentKind.Deck, Name = deck.Name, SetCode = "SLD", Count = 1 } }
        });
        return scryfall;
    }

    private Blueprint Blueprint(string scryfall)
    {
        var bp = new Blueprint { Id = _nextId++, ExpansionId = 1, Name = scryfall, Version = "", ScryfallId = scryfall };
        _db.Blueprints.Add(bp);
        return bp;
    }

    private void Sell(string scryfall, int quantity, decimal price, bool foil = false, string? tag = null)
    {
        var bp = Blueprint(scryfall);
        _db.OrderItems.Add(new OrderItem { OrderId = 1, BlueprintId = bp.Id, Quantity = quantity, Price = price, IsFoil = foil, Name = scryfall, Tag = tag });
    }

    private void Stock(string scryfall, int quantity, decimal price, bool foil = false)
    {
        var bp = Blueprint(scryfall);
        _db.InventoryItems.Add(new InventoryItem
        {
            BlueprintId = bp.Id, Quantity = quantity, ListingPrice = price, IsFoil = foil,
            Condition = "Near Mint", Language = "English", Location = "", DateAdded = new DateTime(2026, 5, 1)
        });
    }

    private async Task SeedBaseAsync()
    {
        _db.Expansions.Add(new Expansion { Id = 1, Name = "Secret Lair Drop Series", Code = "sld" });
        // Commissione Card Trader al 10% per conti tondi.
        _db.Orders.Add(new Order { Id = 1, Code = "A", PaidAt = new DateTime(2026, 6, 1), SellerFee = 10m, SellerSubtotal = 100m });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Un_drop_con_tutte_le_carte_caricate_conta_al_prezzo_standard()
    {
        await SeedBaseAsync();
        var cards = AddDrop(1, "Secret Lair Drop Kitties", 4);
        Sell(cards[0], 2, 30m);
        Sell(cards[1], 2, 10m);
        Stock(cards[2], 2, 5m);
        Stock(cards[3], 3, 5m); // una copia sciolta in più non fa un terzo drop
        await _db.SaveChangesAsync();

        var result = await Service().GetAsync();

        var drop = result.Drops.Should().ContainSingle().Subject;
        drop.Type.Should().Be(SecretLairDropType.Normal);
        drop.Copies.Should().Be(2, "il minimo di copie fra le carte del drop");
        drop.UnitPrice.Should().Be(34.99m);
        drop.Cost.Should().Be(69.98m);
        drop.NetRevenue.Should().Be(72m, "80 € venduti meno il 10% di commissione");
        drop.StockListingValue.Should().Be(25m);
        drop.ProfitWithStock.Should().Be(Math.Round(72m + 22.5m - 69.98m, 2));
        drop.CardsListed.Should().Be(4);
    }

    [Fact]
    public async Task Le_carte_di_un_drop_incompleto_sono_singole_sciolte()
    {
        await SeedBaseAsync();
        var cards = AddDrop(1, "Secret Lair Drop Partial", 5);
        Sell(cards[0], 1, 20m);
        Sell(cards[1], 1, 8m);
        await _db.SaveChangesAsync();

        var result = await Service().GetAsync();

        result.Drops.Should().BeEmpty();
        result.LooseSoldCopies.Should().Be(2);
        result.LooseGrossRevenue.Should().Be(28m);
    }

    [Fact]
    public async Task Un_drop_da_una_carta_non_prova_un_acquisto_intero()
    {
        await SeedBaseAsync();
        var cards = AddDrop(1, "Secret Lair Drop Astrology Lands Virgo", 1);
        Sell(cards[0], 1, 2m);
        await _db.SaveChangesAsync();

        (await Service().GetAsync()).Drops.Should().BeEmpty();
    }

    [Fact]
    public async Task Il_registro_acquisti_prevale_su_stima_e_prezzo_standard()
    {
        await SeedBaseAsync();
        var cards = AddDrop(1, "Secret Lair Drop Showcase Foil", 3, foil: true);
        Sell(cards[0], 1, 50m, foil: true); // drop incompleto, ma registrato
        _db.ProductPurchases.Add(new ProductPurchase { SealedProductId = 1, Quantity = 3, UnitPrice = 40m });
        await _db.SaveChangesAsync();

        var drop = (await Service().GetAsync()).Drops.Should().ContainSingle().Subject;

        drop.Type.Should().Be(SecretLairDropType.Foil);
        drop.RegisteredPurchase.Should().BeTrue();
        drop.Copies.Should().Be(3);
        drop.Cost.Should().Be(120m);
    }

    [Fact]
    public async Task Le_carte_col_tag_del_drop_registrato_vanno_a_quel_drop()
    {
        await SeedBaseAsync();
        AddDrop(1, "Secret Lair Drop Tagged", 3);
        _db.ProductPurchases.Add(new ProductPurchase { SealedProductId = 1, Quantity = 1, UnitPrice = 34.99m, Tag = "#SLD_TAG_20261101" });
        Sell("carta-non-in-mtgjson", 1, 15m, tag: "sld_tag_20261101");
        await _db.SaveChangesAsync();

        var drop = (await Service().GetAsync()).Drops.Should().ContainSingle().Subject;

        drop.SoldCopies.Should().Be(1);
        drop.GrossRevenue.Should().Be(15m);
    }

    [Fact]
    public async Task La_versione_foil_non_si_confonde_con_quella_normale()
    {
        await SeedBaseAsync();
        var normal = AddDrop(1, "Secret Lair Drop Same", 3);
        Sell(normal[0], 1, 10m, foil: true);
        Sell(normal[1], 1, 10m, foil: true);
        Sell(normal[2], 1, 10m, foil: true);
        await _db.SaveChangesAsync();

        var result = await Service().GetAsync();

        result.Drops.Should().BeEmpty("il drop è non foil e le carte vendute sono foil");
        result.LooseSoldCopies.Should().Be(3);
    }
}
