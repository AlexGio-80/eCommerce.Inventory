using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.MtgJson;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// La convenienza fra formati si basa sulla scomposizione in buste: un errore lì fa sembrare
/// conveniente un prodotto che non lo è. I casi sono presi dall'uscita di Star Trek.
/// </summary>
public class SealedProductAnalysisServiceTests
{
    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    private static readonly Guid PlayPack = Guid.NewGuid();
    private static readonly Guid CollectorPack = Guid.NewGuid();
    private static readonly Guid PlayBox = Guid.NewGuid();
    private static readonly Guid CollectorBox = Guid.NewGuid();
    private static readonly Guid PlayBoxCase = Guid.NewGuid();
    private static readonly Guid DraftNight = Guid.NewGuid();
    private static readonly Guid SceneBox = Guid.NewGuid();
    private static readonly Guid CommanderDeck = Guid.NewGuid();

    private static SealedProduct Product(Guid uuid, string set, string name, string category, int? cmId,
        params SealedProductContent[] contents) => new()
    {
        Uuid = uuid, SetCode = set, Name = name, Category = category, CardmarketId = cmId, Contents = contents.ToList()
    };

    private static SealedProductContent Pack(string code) => new() { Kind = SealedContentKind.Pack, PackCode = code, SetCode = "TRK" };
    private static SealedProductContent Sealed(Guid child, int count) => new() { Kind = SealedContentKind.Sealed, ChildUuid = child, Count = count };
    private static SealedProductContent Other(string name) => new() { Kind = SealedContentKind.Other, Name = name };
    private static SealedProductContent Deck(string name) => new() { Kind = SealedContentKind.Deck, Name = name };

    private static List<SealedProduct> StarTrek() => new()
    {
        Product(PlayPack, "TRK", "Star Trek Play Booster Pack", "booster_pack", 1, Pack("play")),
        Product(CollectorPack, "TRK", "Star Trek Collector Booster Pack", "booster_pack", 2, Pack("collector")),
        Product(PlayBox, "TRK", "Star Trek Play Booster Box", "booster_box", 3, Sealed(PlayPack, 30)),
        Product(CollectorBox, "TRK", "Star Trek Collector Booster Box", "booster_box", 4, Sealed(CollectorPack, 12)),
        Product(PlayBoxCase, "TRK", "Star Trek Play Booster Box Case", "booster_case", null, Sealed(PlayBox, 6)),
        Product(DraftNight, "TRK", "Star Trek Draft Night", "limited_aid_tool", 5,
            Sealed(PlayPack, 12), Sealed(CollectorPack, 1), Other("90 Non-foil basic lands")),
        Product(SceneBox, "TRK", "Star Trek Scene Box Enterprise-Q", "box_set", 6,
            Deck("Enterprise-Q"), Sealed(PlayPack, 3)),
        Product(CommanderDeck, "TRC", "Star Trek Commander Deck We Are the Borg", "deck", 7, Deck("We Are the Borg"))
    };

    [Fact]
    public void Resolve_FollowsNestedProducts_DownToThePacks()
    {
        var catalog = StarTrek().ToDictionary(p => p.Uuid);

        var composition = SealedProductAnalysisService.Resolve(catalog[PlayBoxCase], catalog);

        composition.Packs.Should().Equal(new Dictionary<string, int> { ["TRK:play"] = 180 });
        composition.IsPurePacks.Should().BeTrue();
    }

    [Fact]
    public void Resolve_DraftNight_IsPlayAndCollectorPacksWithExtras()
    {
        var catalog = StarTrek().ToDictionary(p => p.Uuid);

        var composition = SealedProductAnalysisService.Resolve(catalog[DraftNight], catalog);

        composition.Packs.Should().Equal(new Dictionary<string, int> { ["TRK:play"] = 12, ["TRK:collector"] = 1 });
        composition.HasExtras.Should().BeTrue();
        composition.IsPurePacks.Should().BeTrue("le terre base non hanno valore di rivendita autonomo");
    }

    [Fact]
    public void Resolve_BundleLandPack_IsAnExtra_NotADeck()
    {
        var bundle = Product(Guid.NewGuid(), "FRA", "Reality Fracture Bundle", "bundle", null,
            Sealed(PlayPack, 9), Deck("Reality Fracture Bundle Land Pack"));
        var catalog = StarTrek().Append(bundle).ToDictionary(p => p.Uuid);

        var composition = SealedProductAnalysisService.Resolve(bundle, catalog);

        composition.HasDeck.Should().BeFalse();
        composition.HasExtras.Should().BeTrue();
        composition.IsPurePacks.Should().BeTrue("le terre del bundle non devono escluderlo dal confronto");
    }

    [Fact]
    public void Resolve_MissingChildProduct_IsMarkedUnresolved()
    {
        var orphan = Product(Guid.NewGuid(), "TRK", "Orfano", "bundle", null, Sealed(Guid.NewGuid(), 2));

        var composition = SealedProductAnalysisService.Resolve(orphan, new Dictionary<Guid, SealedProduct>());

        composition.Unresolved.Should().BeTrue();
        composition.IsPurePacks.Should().BeFalse();
    }

    [Fact]
    public void ComputeReferences_TakesTheCheapestPerPack_AmongSingleTypeProducts()
    {
        var catalog = StarTrek().ToDictionary(p => p.Uuid);
        (string, PackComposition, decimal?) Row(Guid id, decimal? price) =>
            (catalog[id].Name, SealedProductAnalysisService.Resolve(catalog[id], catalog), price);

        var references = SealedProductAnalysisService.ComputeReferences(new[]
        {
            Row(PlayPack, 5.90m),
            Row(PlayBox, 140.35m),
            Row(CollectorPack, 43.49m),
            Row(CollectorBox, 428.32m),
            Row(DraftNight, 50m),     // mista: non può fare da riferimento anche se costa poco
            Row(SceneBox, 1m)          // contiene un mazzo: esclusa
        });

        references["TRK:play"].Should().Be(new PackReference(4.68m, "Star Trek Play Booster Box"));
        references["TRK:collector"].Should().Be(new PackReference(35.69m, "Star Trek Collector Booster Box"));
    }

    [Fact]
    public async Task Analyze_ComparesEachProductWithItsPacksAtReferencePrices()
    {
        _db.MtgjsonSets.AddRange(
            new MtgjsonSet { Code = "TRK", Name = "Star Trek" },
            new MtgjsonSet { Code = "TRC", Name = "Star Trek Commander", ParentCode = "TRK" });
        _db.SealedProducts.AddRange(StarTrek());
        var date = new DateOnly(2026, 10, 7);
        _db.CardmarketPriceSnapshots.AddRange(
            new CardmarketPriceSnapshot { IdProduct = 1, Date = date, Trend = 5.90m },
            new CardmarketPriceSnapshot { IdProduct = 2, Date = date, Trend = 43.49m },
            new CardmarketPriceSnapshot { IdProduct = 3, Date = date.AddDays(-1), Trend = 150m },
            new CardmarketPriceSnapshot { IdProduct = 3, Date = date, Trend = 140.35m, Low = 135m },
            new CardmarketPriceSnapshot { IdProduct = 4, Date = date, Trend = 428.32m },
            new CardmarketPriceSnapshot { IdProduct = 5, Date = date, Trend = 98.21m },
            new CardmarketPriceSnapshot { IdProduct = 6, Date = date, Trend = 40.69m },
            new CardmarketPriceSnapshot { IdProduct = 7, Date = date, Trend = 74.92m });
        await _db.SaveChangesAsync();

        var service = new SealedProductAnalysisService(_db, Mock.Of<ICardTraderApiService>(),
            new BulkSellThroughService(_db), new PriceRealizationService(_db), new ConfigurationBuilder().Build(),
            NullLogger<SealedProductAnalysisService>.Instance);
        var analysis = (await service.AnalyzeAsync("trk"))!;

        analysis.ChildSets.Should().Equal("Star Trek Commander");
        analysis.Products.Should().Contain(p => p.Name.Contains("Commander Deck"), "i set figli fanno parte dell'uscita");

        var playBox = analysis.Products.Single(p => p.Name == "Star Trek Play Booster Box");
        playBox.CmTrend.Should().Be(140.35m, "conta l'ultimo listino, non quelli precedenti");
        playBox.CmLow.Should().Be(135m);
        playBox.PricePerPack.Should().Be(4.68m);

        // 12 × 4,68 + 1 × 35,69 = 91,85; la Draft Night a 98,21 costa il 6,9% in più delle sue buste
        var draftNight = analysis.Products.Single(p => p.Name == "Star Trek Draft Night");
        draftNight.ContentsDescription.Should().Be("12 Play + 1 Collector + extra");
        draftNight.PackValue.Should().Be(91.85m);
        draftNight.DeltaPercent.Should().Be(6.9m);

        var sceneBox = analysis.Products.Single(p => p.Name.Contains("Scene Box"));
        sceneBox.HasFixedContent.Should().BeTrue();
        sceneBox.DeltaPercent.Should().BeNull("con un mazzo dentro il confronto con le sole buste non ha senso");

        analysis.Products.Single(p => p.Name.EndsWith("Case")).IsCase.Should().BeTrue();
    }

    [Fact]
    public void MapContents_KeepsEveryKindOfEntry()
    {
        var contents = new MtgJsonSealedContentsDto
        {
            Pack = new() { new MtgJsonPackDto { Code = "play", Set = "trk" } },
            Sealed = new() { new MtgJsonSealedRefDto { Count = 12, Name = "Pack", Set = "trk", Uuid = PlayPack } },
            Deck = new() { new MtgJsonNamedRefDto { Name = "Enterprise-Q", Set = "trk" } },
            Other = new() { new MtgJsonNamedRefDto { Name = "1 Spindown die" } },
            Variable = new() { default }
        };

        var mapped = SealedCatalogImportService.MapContents(contents).ToList();

        mapped.Select(c => c.Kind).Should().Equal(
            SealedContentKind.Pack, SealedContentKind.Sealed, SealedContentKind.Deck,
            SealedContentKind.Other, SealedContentKind.Variable);
        mapped[0].SetCode.Should().Be("TRK");
        mapped[1].Count.Should().Be(12);
        mapped[1].ChildUuid.Should().Be(PlayPack);
    }

    [Theory]
    [InlineData(85.08, 164.50, true)]   // Scene Box Case da 4 box abbinato allo Scene Box Set da 2
    [InlineData(93.62, 561.72, true)]   // Gift Bundle Case abbinato al Gift Bundle singolo
    [InlineData(140.35, 177.00, false)] // Play Box contro 30 buste sciolte: normale sconto del box
    public void IsPriceMismatch_FlagsProductsPricedFarFromWhatTheyContain(double product, double components, bool expected) =>
        SealedProductAnalysisService.IsPriceMismatch((decimal)product, (decimal)components).Should().Be(expected);

    [Theory]
    [InlineData("TRK:play", "TRK", "Play")]
    [InlineData("TRK:collector", "TRK", "Collector")]
    [InlineData("TRC:collector-sample", "TRK", "TRC Collector Sample")]
    public void PackLabel_IsReadable(string key, string mainSet, string expected) =>
        SealedProductAnalysisService.PackLabel(key, mainSet).Should().Be(expected);
}
