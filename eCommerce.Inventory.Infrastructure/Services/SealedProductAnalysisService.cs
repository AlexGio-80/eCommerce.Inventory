using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Convenienza fra i formati di un'uscita (Fase 1 dell'analisi acquisti).
///
/// Ogni prodotto viene scomposto nelle buste che contiene, anche su più livelli (case → box →
/// busta). Per ogni tipo di busta il prezzo di riferimento è il costo per busta più basso fra i
/// prodotti fatti solo di quel tipo, e ogni prodotto si confronta con il valore delle sue buste a
/// quel prezzo. È il confronto che si faceva a mano "a parità di contenuto", ma per busta e non per
/// carta: una carta di una Play Booster e una di una Collector non valgono la stessa cifra.
///
/// Prezzo di riferimento: <c>trend</c> di Cardmarket (dove si compra), con <c>low</c> accanto.
/// </summary>
public class SealedProductAnalysisService
{
    private const int MaxDepth = 6;

    private readonly ApplicationDbContext _db;
    private readonly ICardTraderApiService _cardTrader;
    private readonly ILogger<SealedProductAnalysisService> _logger;

    public SealedProductAnalysisService(
        ApplicationDbContext db,
        ICardTraderApiService cardTrader,
        ILogger<SealedProductAnalysisService> logger)
    {
        _db = db;
        _cardTrader = cardTrader;
        _logger = logger;
    }

    /// <summary>
    /// Uscite analizzabili: le espansioni principali che hanno prodotti sigillati, direttamente o
    /// nei set figli. Le più recenti (e quelle in preordine) per prime.
    /// </summary>
    public async Task<List<SealedSetOption>> GetSetsAsync(CancellationToken cancellationToken = default)
    {
        var sets = await _db.MtgjsonSets.AsNoTracking().ToListAsync(cancellationToken);
        var productCounts = await _db.SealedProducts.AsNoTracking()
            .GroupBy(p => p.SetCode)
            .Select(g => new { SetCode = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SetCode, x => x.Count, cancellationToken);

        var codes = sets.Select(s => s.Code).ToHashSet();

        return sets
            .Where(s => s.ParentCode == null || !codes.Contains(s.ParentCode))
            .Select(s =>
            {
                var group = GroupCodes(s.Code, sets);
                return new SealedSetOption(
                    s.Code, s.Name, s.ReleaseDate,
                    group.Sum(c => productCounts.GetValueOrDefault(c)),
                    sets.Where(c => c.ParentCode == s.Code).Select(c => c.Name).ToList());
            })
            .Where(o => o.ProductCount > 0)
            .OrderByDescending(o => o.ReleaseDate)
            .ToList();
    }

    public async Task<SealedSetAnalysis?> AnalyzeAsync(string setCode, CancellationToken cancellationToken = default)
    {
        setCode = setCode.ToUpperInvariant();
        var sets = await _db.MtgjsonSets.AsNoTracking().ToListAsync(cancellationToken);
        var main = sets.FirstOrDefault(s => s.Code == setCode);
        if (main == null) return null;

        var groupCodes = GroupCodes(setCode, sets);

        // Tutto il catalogo in memoria (qualche migliaio di righe): i prodotti di un'uscita possono
        // contenere prodotti di un altro set, e la scomposizione li deve trovare.
        var catalog = await _db.SealedProducts.AsNoTracking().Include(p => p.Contents)
            .ToDictionaryAsync(p => p.Uuid, cancellationToken);

        var products = catalog.Values.Where(p => groupCodes.Contains(p.SetCode)).ToList();
        var prices = await LoadLatestCardmarketPricesAsync(
            products.Where(p => p.CardmarketId.HasValue).Select(p => p.CardmarketId!.Value).ToList(),
            cancellationToken);

        var rows = products.Select(p =>
        {
            var price = p.CardmarketId.HasValue ? prices.GetValueOrDefault(p.CardmarketId.Value) : null;
            return (Product: p, Composition: Resolve(p, catalog), Price: price);
        }).ToList();

        var references = ComputeReferences(rows.Select(r => (r.Product.Name, r.Composition, r.Price?.Trend)));

        var productDtos = rows
            .Select(r => BuildProductDto(r.Product, r.Composition, r.Price, references, setCode))
            .OrderBy(d => CategoryOrder(d.Category))
            .ThenBy(d => d.Name)
            .ToList();

        var referenceDtos = references
            .Select(kv => new PackReferenceDto(kv.Key, PackLabel(kv.Key, setCode), kv.Value.PricePerPack, kv.Value.ProductName))
            .OrderBy(r => r.Label)
            .ToList();

        return new SealedSetAnalysis(
            main.Code, main.Name, main.ReleaseDate,
            sets.Where(s => groupCodes.Contains(s.Code) && s.Code != main.Code).Select(s => s.Name).ToList(),
            prices.Values.Select(p => (DateOnly?)p.Date).DefaultIfEmpty(null).Max(),
            products.Select(p => (DateTime?)p.LastImportedAt).DefaultIfEmpty(null).Max(),
            referenceDtos,
            productDtos);
    }

    /// <summary>
    /// Aggiorna il prezzo Card Trader dei prodotti di un'uscita: una chiamata al marketplace per
    /// ciascuna espansione Card Trader coinvolta (in genere due: l'espansione e il suo Commander).
    /// </summary>
    public async Task<CardTraderSealedRefreshResult> RefreshCardTraderPricesAsync(string setCode, CancellationToken cancellationToken = default)
    {
        setCode = setCode.ToUpperInvariant();
        var sets = await _db.MtgjsonSets.AsNoTracking().ToListAsync(cancellationToken);
        var groupCodes = GroupCodes(setCode, sets);

        var products = await _db.SealedProducts
            .Where(p => groupCodes.Contains(p.SetCode) && p.CardTraderBlueprintId != null)
            .ToListAsync(cancellationToken);
        var blueprintIds = products.Select(p => p.CardTraderBlueprintId!.Value).Distinct().ToList();

        // Il marketplace si interroga per espansione Card Trader: la si ricava dai blueprint
        // sincronizzati. Quelli non ancora sincronizzati si chiedono uno per uno.
        var expansionByBlueprint = await _db.Blueprints.AsNoTracking()
            .Where(b => blueprintIds.Contains(b.CardTraderId))
            .Select(b => new { b.CardTraderId, ExpansionCtId = b.Expansion.CardTraderId })
            .ToDictionaryAsync(b => b.CardTraderId, b => b.ExpansionCtId, cancellationToken);

        var offers = new List<CardTraderMarketplaceProductDto>();
        var calls = 0;

        foreach (var expansionId in expansionByBlueprint.Values.Distinct())
        {
            offers.AddRange(await _cardTrader.GetMarketplaceProductsByExpansionAsync(expansionId, cancellationToken));
            calls++;
        }

        foreach (var blueprintId in blueprintIds.Where(id => !expansionByBlueprint.ContainsKey(id)))
        {
            offers.AddRange(await _cardTrader.GetMarketplaceProductsAsync(blueprintId, cancellationToken));
            calls++;
        }

        var byBlueprint = offers
            .Where(o => blueprintIds.Contains(o.BlueprintId) && o.PriceCents > 0 && o.Quantity > 0 && !o.OnVacation)
            .Where(o => IsEnglish(o.Properties.Language))
            .GroupBy(o => o.BlueprintId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var now = DateTime.UtcNow;
        var withOffers = 0;
        foreach (var product in products)
        {
            byBlueprint.TryGetValue(product.CardTraderBlueprintId!.Value, out var productOffers);
            product.CtMinPrice = productOffers is { Count: > 0 } ? productOffers.Min(o => o.PriceCents) / 100m : null;
            product.CtOfferCount = productOffers?.Count ?? 0;
            product.CtPriceUpdatedAt = now;
            if (productOffers is { Count: > 0 }) withOffers++;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Prezzi Card Trader dei sigillati di {Set}: {WithOffers}/{Products} prodotti con offerte, {Calls} chiamate",
            setCode, withOffers, products.Count, calls);

        return new CardTraderSealedRefreshResult(products.Count, withOffers, calls);
    }

    /// <summary>Il set e i suoi figli diretti (es. TRK e TRC).</summary>
    private static HashSet<string> GroupCodes(string code, IEnumerable<MtgjsonSet> sets) =>
        sets.Where(s => s.Code == code || s.ParentCode == code).Select(s => s.Code).ToHashSet();

    /// <summary>
    /// Si compra solo in inglese. Un'offerta senza lingua indicata è accettata: non è verificato
    /// che Card Trader la riporti su tutti i sigillati, e scartarle svuoterebbe il confronto.
    /// </summary>
    private static bool IsEnglish(string? language) =>
        string.IsNullOrEmpty(language)
        || string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "English", StringComparison.OrdinalIgnoreCase);

    private async Task<Dictionary<int, CardmarketPriceSnapshot>> LoadLatestCardmarketPricesAsync(
        List<int> ids, CancellationToken cancellationToken)
    {
        var latestDates = _db.CardmarketPriceSnapshots
            .Where(s => ids.Contains(s.IdProduct))
            .GroupBy(s => s.IdProduct)
            .Select(g => new { IdProduct = g.Key, Date = g.Max(s => s.Date) });

        return await (
                from s in _db.CardmarketPriceSnapshots
                join l in latestDates on new { s.IdProduct, s.Date } equals new { l.IdProduct, l.Date }
                select s)
            .AsNoTracking()
            .ToDictionaryAsync(s => s.IdProduct, cancellationToken);
    }

    /// <summary>
    /// Scompone un prodotto nelle buste che contiene. Le buste sono identificate da set e tipo
    /// (es. "TRK:play"); mazzi, carte ed extra non sono buste e vengono solo segnalati.
    /// </summary>
    public static PackComposition Resolve(SealedProduct product, IReadOnlyDictionary<Guid, SealedProduct> catalog) =>
        Resolve(product, catalog, 0);

    private static PackComposition Resolve(SealedProduct product, IReadOnlyDictionary<Guid, SealedProduct> catalog, int depth)
    {
        var result = new PackComposition();
        if (depth > MaxDepth)
        {
            result.Unresolved = true;
            return result;
        }

        foreach (var content in product.Contents)
        {
            switch (content.Kind)
            {
                case SealedContentKind.Pack:
                    result.AddPacks($"{content.SetCode ?? product.SetCode}:{content.PackCode}", 1);
                    break;

                case SealedContentKind.Sealed:
                    if (content.ChildUuid is { } childUuid && catalog.TryGetValue(childUuid, out var child))
                        result.Merge(Resolve(child, catalog, depth + 1), content.Count);
                    else
                        result.Unresolved = true;
                    break;

                case SealedContentKind.Deck:
                    // MTGJSON registra le terre dei bundle come mazzo ("... Bundle Land Pack"): sono
                    // extra come le terre base, e trattarle da mazzo escluderebbe i bundle dal confronto.
                    if (IsLandPack(content.Name)) result.HasExtras = true;
                    else result.HasDeck = true;
                    break;

                case SealedContentKind.Card:
                    result.HasCards = true;
                    break;

                case SealedContentKind.Other:
                    result.HasExtras = true;
                    break;

                case SealedContentKind.Variable:
                    result.Unresolved = true;
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Prezzo di riferimento per ogni tipo di busta: il costo per busta più basso fra i prodotti
    /// fatti solo di quel tipo. Gli extra (terre, dadi) sono ammessi, mazzi e carte no: il loro
    /// valore falserebbe il costo della busta.
    /// </summary>
    public static Dictionary<string, PackReference> ComputeReferences(
        IEnumerable<(string ProductName, PackComposition Composition, decimal? Price)> products)
    {
        var references = new Dictionary<string, PackReference>();

        foreach (var (name, composition, price) in products)
        {
            if (price is not > 0 || !composition.IsPurePacks || composition.Packs.Count != 1) continue;

            var (packKey, count) = composition.Packs.Single();
            var perPack = Math.Round(price.Value / count, 2);

            if (!references.TryGetValue(packKey, out var current) || perPack < current.PricePerPack)
                references[packKey] = new PackReference(perPack, name);
        }

        return references;
    }

    private static SealedProductAnalysisDto BuildProductDto(
        SealedProduct product,
        PackComposition composition,
        CardmarketPriceSnapshot? price,
        IReadOnlyDictionary<string, PackReference> references,
        string mainSetCode)
    {
        var packs = composition.Packs
            .Select(kv => new PackCountDto(kv.Key, PackLabel(kv.Key, mainSetCode), kv.Value))
            .OrderByDescending(p => p.Count)
            .ToList();

        var totalPacks = composition.Packs.Values.Sum();
        decimal? pricePerPack = price?.Trend is > 0 && composition.Packs.Count == 1 && composition.IsPurePacks
            ? Math.Round(price.Trend.Value / totalPacks, 2)
            : null;

        // Valore delle buste ai prezzi di riferimento: solo se tutte le buste hanno un riferimento.
        decimal? packValue = totalPacks > 0 && !composition.Unresolved && composition.Packs.Keys.All(references.ContainsKey)
            ? composition.Packs.Sum(kv => kv.Value * references[kv.Key].PricePerPack)
            : null;

        // Lo scarto ha senso solo se il prodotto è fatto di buste (più eventuali extra): con un mazzo
        // o carte specifiche dentro, il confronto con le sole buste direbbe che è carissimo.
        decimal? deltaPercent = packValue is > 0 && price?.Trend is > 0 && composition.IsPurePacks
            ? Math.Round((price.Trend.Value - packValue.Value) / packValue.Value * 100m, 1)
            : null;

        return new SealedProductAnalysisDto(
            product.Id, product.Name, product.Category, product.Subtype, product.SetCode,
            DescribeContents(product, composition, packs),
            packs, totalPacks,
            composition.HasDeck || composition.HasCards, composition.HasExtras, composition.Unresolved,
            IsCase(product.Category),
            product.CardmarketId, product.CardTraderBlueprintId,
            price?.Trend, price?.Low, price?.Date,
            product.CtMinPrice, product.CtOfferCount, product.CtPriceUpdatedAt,
            pricePerPack, packValue, deltaPercent);
    }

    private static string DescribeContents(SealedProduct product, PackComposition composition, List<PackCountDto> packs)
    {
        var parts = packs.Select(p => $"{p.Count} {p.Label}").ToList();

        var decks = product.Contents.Where(c => c.Kind == SealedContentKind.Deck && !IsLandPack(c.Name))
            .Select(c => $"mazzo {c.Name}").ToList();
        parts.AddRange(decks);
        if (decks.Count == 0 && composition.HasDeck) parts.Add("mazzi");

        var cards = product.Contents.Count(c => c.Kind == SealedContentKind.Card);
        if (cards > 0) parts.Add(cards == 1 ? "1 carta" : $"{cards} carte");
        else if (composition.HasCards) parts.Add("carte");

        if (composition.HasExtras) parts.Add("extra");
        if (composition.Unresolved) parts.Add("contenuto non scomponibile");

        return parts.Count == 0 ? "Contenuto non indicato da MTGJSON" : string.Join(" + ", parts);
    }

    /// <summary>"TRK:play" → "Play"; con un set diverso da quello principale → "TRC Play".</summary>
    public static string PackLabel(string packKey, string mainSetCode)
    {
        var separator = packKey.IndexOf(':');
        var set = separator > 0 ? packKey[..separator] : string.Empty;
        var code = separator >= 0 ? packKey[(separator + 1)..] : packKey;
        var label = string.Join(' ', code.Split('-', '_', ' ')
            .Where(w => w.Length > 0)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

        return string.Equals(set, mainSetCode, StringComparison.OrdinalIgnoreCase) || set.Length == 0
            ? label
            : $"{set} {label}";
    }

    private static bool IsLandPack(string? deckName) =>
        deckName != null && deckName.EndsWith("Land Pack", StringComparison.OrdinalIgnoreCase);

    private static bool IsCase(string? category) =>
        category != null && category.EndsWith("_case", StringComparison.OrdinalIgnoreCase);

    private static int CategoryOrder(string? category) => category switch
    {
        "booster_box" => 0,
        "booster_pack" => 1,
        "bundle" => 2,
        "limited_aid_tool" => 3,
        "box_set" => 4,
        "deck" => 5,
        "subset" => 6,
        _ when IsCase(category) => 9,
        _ => 7
    };
}

/// <summary>Buste contenute in un prodotto, più ciò che non è una busta.</summary>
public class PackComposition
{
    public Dictionary<string, int> Packs { get; } = new();
    public bool HasDeck { get; set; }
    public bool HasCards { get; set; }
    public bool HasExtras { get; set; }
    public bool Unresolved { get; set; }

    /// <summary>Solo buste (con eventuali extra senza valore di rivendita).</summary>
    public bool IsPurePacks => Packs.Count > 0 && !HasDeck && !HasCards && !Unresolved;

    public void AddPacks(string packKey, int count) =>
        Packs[packKey] = Packs.GetValueOrDefault(packKey) + count;

    public void Merge(PackComposition child, int times)
    {
        foreach (var (key, count) in child.Packs) AddPacks(key, count * times);
        HasDeck |= child.HasDeck;
        HasCards |= child.HasCards;
        HasExtras |= child.HasExtras;
        Unresolved |= child.Unresolved;
    }
}

public record PackReference(decimal PricePerPack, string ProductName);

public record SealedSetOption(string Code, string Name, DateOnly? ReleaseDate, int ProductCount, List<string> ChildSets);

public record PackReferenceDto(string PackKey, string Label, decimal PricePerPack, string ProductName);

public record PackCountDto(string PackKey, string Label, int Count);

public record SealedProductAnalysisDto(
    int Id,
    string Name,
    string? Category,
    string? Subtype,
    string SetCode,
    string ContentsDescription,
    List<PackCountDto> Packs,
    int TotalPacks,
    bool HasFixedContent,
    bool HasExtras,
    bool Unresolved,
    bool IsCase,
    int? CardmarketId,
    int? CardTraderBlueprintId,
    decimal? CmTrend,
    decimal? CmLow,
    DateOnly? CmPriceDate,
    decimal? CtMinPrice,
    int? CtOfferCount,
    DateTime? CtPriceUpdatedAt,
    decimal? PricePerPack,
    decimal? PackValue,
    decimal? DeltaPercent);

public record SealedSetAnalysis(
    string Code,
    string Name,
    DateOnly? ReleaseDate,
    List<string> ChildSets,
    DateOnly? CardmarketPriceDate,
    DateTime? CatalogImportedAt,
    List<PackReferenceDto> References,
    List<SealedProductAnalysisDto> Products);

public record CardTraderSealedRefreshResult(int Products, int ProductsWithOffers, int ApiCalls);
