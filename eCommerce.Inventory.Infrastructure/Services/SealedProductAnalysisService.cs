using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private readonly BulkSellThroughService _bulkSellThrough;
    private readonly PriceRealizationService _priceRealization;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SealedProductAnalysisService> _logger;

    public SealedProductAnalysisService(
        ApplicationDbContext db,
        ICardTraderApiService cardTrader,
        BulkSellThroughService bulkSellThrough,
        PriceRealizationService priceRealization,
        IConfiguration configuration,
        ILogger<SealedProductAnalysisService> logger)
    {
        _db = db;
        _cardTrader = cardTrader;
        _bulkSellThrough = bulkSellThrough;
        _priceRealization = priceRealization;
        _configuration = configuration;
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

    public async Task<SealedSetAnalysis?> AnalyzeAsync(
        string setCode,
        OpeningValueOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(overrides, cancellationToken);
        return await AnalyzeCoreAsync(setCode, context, cancellationToken);
    }

    /// <summary>
    /// Analisi in serie di più uscite (classifica delle opportunità): catalogo e parametri si
    /// caricano una volta sola invece che per ogni uscita.
    /// </summary>
    public async Task<List<SealedSetAnalysis>> AnalyzeManyAsync(IEnumerable<string> setCodes, CancellationToken cancellationToken = default)
    {
        var context = await LoadContextAsync(null, cancellationToken);
        var result = new List<SealedSetAnalysis>();
        foreach (var code in setCodes)
        {
            var analysis = await AnalyzeCoreAsync(code, context, cancellationToken);
            if (analysis != null) result.Add(analysis);
        }
        return result;
    }

    private record AnalysisContext(
        List<MtgjsonSet> Sets,
        Dictionary<Guid, SealedProduct> Catalog,
        (OpeningValueSettings Values, OpeningValueSettingsDto Dto) Settings);

    private async Task<AnalysisContext> LoadContextAsync(OpeningValueOverrides? overrides, CancellationToken cancellationToken)
    {
        var sets = await _db.MtgjsonSets.AsNoTracking().ToListAsync(cancellationToken);

        // Tutto il catalogo in memoria (qualche migliaio di righe): i prodotti di un'uscita possono
        // contenere prodotti di un altro set, e la scomposizione li deve trovare.
        var catalog = await _db.SealedProducts.AsNoTracking().Include(p => p.Contents)
            .ToDictionaryAsync(p => p.Uuid, cancellationToken);

        var settings = await ResolveSettingsAsync(overrides, cancellationToken);
        return new AnalysisContext(sets, catalog, settings);
    }

    private async Task<SealedSetAnalysis?> AnalyzeCoreAsync(string setCode, AnalysisContext context, CancellationToken cancellationToken)
    {
        setCode = setCode.ToUpperInvariant();
        var sets = context.Sets;
        var main = sets.FirstOrDefault(s => s.Code == setCode);
        if (main == null) return null;

        var groupCodes = GroupCodes(setCode, sets);
        var catalog = context.Catalog;

        var products = catalog.Values.Where(p => groupCodes.Contains(p.SetCode)).ToList();
        var cmIds = products.Where(p => p.CardmarketId.HasValue).Select(p => p.CardmarketId!.Value).ToList();
        var prices = await LoadLatestCardmarketPricesAsync(cmIds, cancellationToken);

        // Nome del prodotto su Cardmarket, per cercarlo sul sito: quello MTGJSON a volte è diverso
        // ("Collector Booster Pack" contro "Collector Booster").
        var cmNames = await _db.CardmarketProducts.AsNoTracking()
            .Where(p => cmIds.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, p => p.Name, cancellationToken);

        var rows = products.Select(p =>
        {
            var price = p.CardmarketId.HasValue ? prices.GetValueOrDefault(p.CardmarketId.Value) : null;
            return (Product: p, Composition: Resolve(p, catalog), Price: price);
        }).ToList();

        var references = ComputeReferences(rows.Select(r => (r.Product.Name, r.Composition, r.Price?.Trend)));

        var settings = context.Settings;
        var opening = await BuildCalculatorsAsync(rows.Select(r => r.Composition).ToList(), groupCodes, settings.Values, cancellationToken);

        var productDtos = rows
            .Select(r => BuildProductDto(r.Product, r.Composition, r.Price, references, setCode, opening, settings.Values,
                ComponentsTrend(r.Product, catalog, prices),
                r.Product.CardmarketId is { } cmId ? cmNames.GetValueOrDefault(cmId) : null,
                IsNotPlayable(r.Product.SetCode, sets)))
            .OrderBy(d => CategoryOrder(d.Category))
            .ThenBy(d => d.Name)
            .ToList();

        var referenceDtos = references
            .Select(kv => new PackReferenceDto(kv.Key, PackLabel(kv.Key, setCode), kv.Value.PricePerPack, kv.Value.ProductName))
            .OrderBy(r => r.Label)
            .ToList();

        var packKeys = rows.SelectMany(r => r.Composition.Packs.Keys).Distinct().OrderBy(k => k).ToList();
        var packValues = packKeys
            .Select(key => BuildPackValueDto(key, setCode, opening, settings.Values))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var groupSets = sets.Where(s => groupCodes.Contains(s.Code)).ToList();

        return new SealedSetAnalysis(
            main.Code, main.Name, main.ReleaseDate,
            groupSets.Where(s => s.Code != main.Code).Select(s => s.Name).ToList(),
            prices.Values.Select(p => (DateOnly?)p.Date).DefaultIfEmpty(null).Max(),
            products.Select(p => (DateTime?)p.LastImportedAt).DefaultIfEmpty(null).Max(),
            referenceDtos,
            productDtos,
            groupSets.Any(s => s.DetailImportedAt == null) ? null : groupSets.Min(s => s.DetailImportedAt),
            groupSets.Any(s => s.HasBoosterData),
            settings.Dto,
            packValues);
    }

    /// <summary>Parametri del valore atteso: quelli passati dalla pagina, altrimenti configurazione e dati misurati.</summary>
    private async Task<(OpeningValueSettings Values, OpeningValueSettingsDto Dto)> ResolveSettingsAsync(
        OpeningValueOverrides? overrides, CancellationToken cancellationToken)
    {
        var threshold = overrides?.BulkThreshold ?? _configuration.GetValue("Purchasing:BulkThreshold", 0.25m);
        var bulkPrice = overrides?.BulkPrice ?? _configuration.GetValue("Purchasing:BulkPrice", 0.05m);
        var costPercent = overrides?.SellingCostPercent ?? _configuration.GetValue("Purchasing:SellingCostPercent", 15m);
        var costPerCard = overrides?.CostPerCard ?? _configuration.GetValue("Purchasing:CostPerCard", 0.15m);

        var measured = await _bulkSellThrough.MeasureAsync(threshold, cancellationToken);
        var sellThrough = overrides?.BulkSellThroughPercent is { } percent ? percent / 100m : measured.Share;
        var bands = await _bulkSellThrough.MeasureBandsAsync(threshold, measured, cancellationToken);

        var realization = await _priceRealization.MeasureAsync(cancellationToken);
        var priceFactor = overrides?.PriceRealizationPercent is { } factorPercent ? factorPercent / 100m : realization.Factor;

        // Commissione reale di Card Trader dagli ordini: solo informativa, accanto al costo scelto.
        var fees = await _db.Orders.AsNoTracking()
            .Where(o => o.PaidAt != null)
            .GroupBy(o => 1)
            .Select(g => new { Fee = g.Sum(o => o.SellerFee), Subtotal = g.Sum(o => o.SellerSubtotal) })
            .FirstOrDefaultAsync(cancellationToken);

        var values = new OpeningValueSettings(threshold, bulkPrice, sellThrough, costPercent, priceFactor, costPerCard,
            bands.Select(b => new SellThroughBand(b.From, b.Share)).ToList());
        var dto = new OpeningValueSettingsDto(
            threshold, bulkPrice, Math.Round(sellThrough * 100m, 1), costPercent,
            Math.Round(measured.Share * 100m, 1), measured.Measured,
            measured.Expansions.Select(e => new BulkSellThroughDto(e.Name, e.ReleaseDate, e.Sold, e.InStock, Math.Round(e.Share * 100m, 1))).ToList(),
            fees is { Subtotal: > 0 } ? Math.Round(fees.Fee / fees.Subtotal * 100m, 2) : null,
            Math.Round(priceFactor * 100m, 1), Math.Round(realization.Factor * 100m, 1), realization.Measured, realization.Copies,
            costPerCard,
            bands.Select(b => new SellThroughBandDto(b.From, b.To, b.Sold, b.InStock, Math.Round(b.Share * 100m, 1), b.Measured)).ToList());

        return (values, dto);
    }

    /// <summary>
    /// Carica composizione delle buste, mazzi, carte e prezzi che servono all'uscita e prepara i due
    /// calcolatori, uno sui prezzi Cardmarket e uno su quelli Card Trader.
    /// </summary>
    private async Task<OpeningCalculators> BuildCalculatorsAsync(
        List<PackComposition> compositions, HashSet<string> groupCodes, OpeningValueSettings settings,
        CancellationToken cancellationToken)
    {
        var packSets = compositions.SelectMany(c => c.Packs.Keys).Select(k => k.Split(':')[0]).Distinct().ToList();
        var deckSets = compositions.SelectMany(c => c.Decks.Keys).Select(k => k.Split(':')[0]).Distinct().ToList();

        var configs = (await _db.BoosterConfigs.AsNoTracking().Include(c => c.Slots)
                .Where(c => packSets.Contains(c.SetCode)).ToListAsync(cancellationToken))
            .GroupBy(c => $"{c.SetCode}:{c.BoosterType}")
            .ToDictionary(g => g.Key, g => g.ToList());

        var sheets = (await _db.BoosterSheets.AsNoTracking().Include(s => s.Cards)
                .Where(s => packSets.Contains(s.SetCode)).ToListAsync(cancellationToken))
            .ToDictionary(s => ($"{s.SetCode}:{s.BoosterType}", s.Name));

        var decks = (await _db.MtgjsonDecks.AsNoTracking().Include(d => d.Cards)
                .Where(d => deckSets.Contains(d.SetCode)).ToListAsync(cancellationToken))
            .GroupBy(d => OpeningValueCalculator.DeckKey(d.SetCode, d.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var cardUuids = sheets.Values.SelectMany(s => s.Cards.Select(c => c.CardUuid))
            .Concat(decks.Values.SelectMany(d => d.Cards.Select(c => c.CardUuid)))
            .Concat(compositions.SelectMany(c => c.Cards.Keys.Select(k => k.Uuid)))
            .Distinct()
            .ToList();

        var cards = await _db.MtgjsonCards.AsNoTracking()
            .Where(c => cardUuids.Contains(c.Uuid))
            .ToDictionaryAsync(c => c.Uuid, cancellationToken);

        var cmIds = cards.Values.Where(c => c.CardmarketId.HasValue).Select(c => c.CardmarketId!.Value).Distinct().ToList();
        var cmPrices = await _db.CardmarketLatestPrices.AsNoTracking()
            .Where(p => cmIds.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, cancellationToken);

        var scryfallIds = cards.Values.Where(c => c.ScryfallId != null).Select(c => c.ScryfallId!).Distinct().ToList();
        var blueprintByScryfall = (await _db.Blueprints.AsNoTracking()
                .Where(b => b.ScryfallId != null && scryfallIds.Contains(b.ScryfallId))
                .Select(b => new { b.ScryfallId, b.CardTraderId })
                .ToListAsync(cancellationToken))
            .GroupBy(b => b.ScryfallId!)
            .ToDictionary(g => g.Key, g => g.First().CardTraderId);
        var blueprintIds = blueprintByScryfall.Values.Distinct().ToList();
        var ctPrices = await _db.CardTraderCardPrices.AsNoTracking()
            .Where(p => blueprintIds.Contains(p.BlueprintId))
            .ToDictionaryAsync(p => (p.BlueprintId, p.IsFoil), cancellationToken);

        decimal? CmPrice(Guid uuid, bool foil)
        {
            if (!cards.TryGetValue(uuid, out var card) || card.CardmarketId is not { } id) return null;
            if (!cmPrices.TryGetValue(id, out var price)) return null;
            return foil ? price.TrendFoil is > 0 ? price.TrendFoil : null : price.Trend is > 0 ? price.Trend : null;
        }

        decimal? CtPrice(Guid uuid, bool foil)
        {
            if (!cards.TryGetValue(uuid, out var card) || card.ScryfallId == null) return null;
            if (!blueprintByScryfall.TryGetValue(card.ScryfallId, out var blueprintId)) return null;
            return ctPrices.TryGetValue((blueprintId, foil), out var price) ? price.Price : null;
        }

        return new OpeningCalculators(
            new OpeningValueCalculator(CmPrice, settings, configs, sheets, decks),
            // Card Trader è il mercato su cui si vende: il fattore misurato rispetto a Cardmarket non vale.
            new OpeningValueCalculator(CtPrice, settings with { PriceFactor = 1m }, configs, sheets, decks),
            cards,
            ctPrices.Count > 0 ? ctPrices.Values.Max(p => p.UpdatedAt) : null);
    }

    /// <summary>
    /// Somma dei trend Cardmarket dei prodotti sigillati contenuti direttamente (es. i 6 bundle di un
    /// case). Null se il prodotto contiene anche altro, se non ne contiene o se uno di loro non ha prezzo.
    /// </summary>
    private static decimal? ComponentsTrend(
        SealedProduct product, IReadOnlyDictionary<Guid, SealedProduct> catalog, IReadOnlyDictionary<int, CardmarketPriceSnapshot> prices)
    {
        // Solo per i prodotti fatti di altri sigillati (più eventuali extra): una Scene Box ha anche un
        // mazzo, e confrontarla con le sole buste la farebbe sembrare abbinata male.
        if (product.Contents.Any(c => c.Kind is not (SealedContentKind.Sealed or SealedContentKind.Other))) return null;

        var children = product.Contents.Where(c => c.Kind == SealedContentKind.Sealed).ToList();
        if (children.Count == 0) return null;

        decimal sum = 0;
        foreach (var child in children)
        {
            if (child.ChildUuid is not { } uuid || !catalog.TryGetValue(uuid, out var childProduct)
                || childProduct.CardmarketId is not { } cmId || !prices.TryGetValue(cmId, out var price) || price.Trend is not > 0)
                return null;
            sum += child.Count * price.Trend.Value;
        }

        return sum;
    }

    /// <summary>
    /// MTGJSON a volte abbina a un case l'id Cardmarket del prodotto singolo o di una confezione
    /// diversa (es. lo Scene Box Case da 4 box abbinato allo "Scene Box Set" da 2): il prezzo letto
    /// non è quello del prodotto, e la decisione sarebbe sbagliata. Si riconosce perché è lontano
    /// dalla somma dei prezzi di ciò che contiene.
    /// </summary>
    public static bool IsPriceMismatch(decimal? productTrend, decimal? componentsTrend) =>
        productTrend is > 0 && componentsTrend is > 0
        && (productTrend < componentsTrend * 0.6m || productTrend > componentsTrend * 1.6m);

    private static PackValueDto? BuildPackValueDto(string packKey, string mainSetCode, OpeningCalculators opening, OpeningValueSettings settings)
    {
        var cm = opening.Cardmarket.Pack(packKey);
        if (cm == null) return null;
        var ct = opening.CardTrader.Pack(packKey);

        return new PackValueDto(
            packKey, PackLabel(packKey, mainSetCode),
            Math.Round(cm.Value, 2), Math.Round(settings.Net(cm.Value), 2), Math.Round(cm.PricedShare * 100m, 1),
            ct is { PricedShare: > 0 } ? Math.Round(ct.Value, 2) : null,
            ct is { PricedShare: > 0 } ? Math.Round(settings.Net(ct.Value), 2) : null,
            ct is null ? 0 : Math.Round(ct.PricedShare * 100m, 1),
            cm.Sheets.Select(s => new SheetValueDto(s.Name, Math.Round(s.SlotsPerPack, 2), Math.Round(s.ValuePerSlot, 2), Math.Round(s.PricedShare * 100m, 1))).ToList(),
            cm.TopCards.Select(c =>
            {
                opening.Cards.TryGetValue(c.Uuid, out var card);
                return new TopCardDto(card?.Name ?? c.Uuid.ToString(), card?.SetCode, card?.Number, c.Foil,
                    Math.Round(c.Value, 2), Math.Round(c.ProbabilityPerPack * 100m, 3), Math.Round(c.ProbabilityPerPack * c.Value, 2));
            }).ToList());
    }

    /// <summary>
    /// Aggiorna i prezzi Card Trader di un'uscita: sigillati e singole (queste ultime per il valore
    /// atteso). Una chiamata al marketplace per ciascuna espansione Card Trader coinvolta: l'espansione,
    /// il suo Commander, le varianti "Collectors" e simili, in genere fra due e sei.
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

        // Singole dell'uscita (carte dei suoi set e dei suoi mazzi), ritrovate su Card Trader per id Scryfall.
        var deckCardUuids = _db.MtgjsonDecks.Where(d => groupCodes.Contains(d.SetCode)).SelectMany(d => d.Cards.Select(c => c.CardUuid));
        var scryfallIds = await _db.MtgjsonCards.AsNoTracking()
            .Where(c => c.ScryfallId != null && (groupCodes.Contains(c.SetCode) || deckCardUuids.Contains(c.Uuid)))
            .Select(c => c.ScryfallId!)
            .Distinct()
            .ToListAsync(cancellationToken);
        var cardBlueprints = await _db.Blueprints.AsNoTracking()
            .Where(b => b.ScryfallId != null && scryfallIds.Contains(b.ScryfallId))
            .Select(b => new { b.CardTraderId, ExpansionCtId = b.Expansion.CardTraderId })
            .ToListAsync(cancellationToken);
        var cardBlueprintIds = cardBlueprints.Select(b => b.CardTraderId).ToHashSet();

        var offers = new List<CardTraderMarketplaceProductDto>();
        var calls = 0;

        foreach (var expansionId in expansionByBlueprint.Values.Concat(cardBlueprints.Select(b => b.ExpansionCtId)).Distinct())
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

        var cardsPriced = await SaveCardPricesAsync(offers, cardBlueprintIds, now, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Prezzi Card Trader di {Set}: {WithOffers}/{Products} sigillati con offerte, {Cards} prezzi di singole, {Calls} chiamate",
            setCode, withOffers, products.Count, cardsPriced, calls);

        return new CardTraderSealedRefreshResult(products.Count, withOffers, calls, cardsPriced);
    }

    /// <summary>
    /// Prezzo di una singola su Card Trader: media delle tre offerte più basse in inglese e Near Mint,
    /// separatamente per foil e non foil. Il minimo da solo dipende troppo da un venditore isolato.
    /// </summary>
    private async Task<int> SaveCardPricesAsync(
        List<CardTraderMarketplaceProductDto> offers, HashSet<int> blueprintIds, DateTime now, CancellationToken cancellationToken)
    {
        var groups = offers
            .Where(o => blueprintIds.Contains(o.BlueprintId) && o.PriceCents > 0 && o.Quantity > 0 && !o.OnVacation)
            .Where(o => IsEnglish(o.Properties.Language))
            .Where(o => string.Equals(o.Properties.Condition, "Near Mint", StringComparison.OrdinalIgnoreCase))
            .GroupBy(o => (o.BlueprintId, o.Properties.IsFoil))
            .ToList();

        var ids = blueprintIds.ToList();
        var existing = await _db.CardTraderCardPrices
            .Where(p => ids.Contains(p.BlueprintId))
            .ToDictionaryAsync(p => (p.BlueprintId, p.IsFoil), cancellationToken);

        foreach (var group in groups)
        {
            var cheapest = group.Select(o => o.PriceCents).OrderBy(c => c).Take(3).ToList();
            if (!existing.Remove(group.Key, out var price))
            {
                price = new CardTraderCardPrice { BlueprintId = group.Key.BlueprintId, IsFoil = group.Key.IsFoil };
                _db.CardTraderCardPrices.Add(price);
            }

            price.Price = Math.Round((decimal)cheapest.Average() / 100m, 2);
            price.OfferCount = group.Count();
            price.UpdatedAt = now;
        }

        // Le carte rimaste senza offerte perdono il prezzo vecchio: meglio "senza prezzo" che un dato stantio.
        _db.CardTraderCardPrices.RemoveRange(existing.Values);

        return groups.Count;
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
                    else
                    {
                        result.HasDeck = true;
                        if (content.Name != null)
                            result.AddDeck(OpeningValueCalculator.DeckKey(content.SetCode ?? product.SetCode, content.Name), 1);
                    }
                    break;

                case SealedContentKind.Card:
                    result.HasCards = true;
                    if (content.ChildUuid is { } cardUuid) result.AddCard(cardUuid, content.Foil == true, 1);
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

    /// <summary>
    /// Uscite che MTGJSON classifica come <c>memorabilia</c>: World Championship Deck, Pro Tour
    /// Collector Set, Collectors' Edition, 30th Anniversary Edition. Carte non giocabili a torneo.
    /// </summary>
    private static bool IsNotPlayable(string setCode, IEnumerable<MtgjsonSet> sets) =>
        string.Equals(sets.FirstOrDefault(s => s.Code == setCode)?.Type, "memorabilia", StringComparison.OrdinalIgnoreCase);

    private static SealedProductAnalysisDto BuildProductDto(
        SealedProduct product,
        PackComposition composition,
        CardmarketPriceSnapshot? price,
        IReadOnlyDictionary<string, PackReference> references,
        string mainSetCode,
        OpeningCalculators opening,
        OpeningValueSettings settings,
        decimal? componentsTrend,
        string? cardmarketName,
        bool notPlayable)
    {
        var priceMismatch = IsPriceMismatch(price?.Trend, componentsTrend);

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
        decimal? deltaPercent = packValue is > 0 && price?.Trend is > 0 && composition.IsPurePacks && !priceMismatch
            ? Math.Round((price.Trend.Value - packValue.Value) / packValue.Value * 100m, 1)
            : null;

        // Valore atteso dell'apertura, al netto dei costi di vendita. Senza nulla da valutare (nessuna
        // busta con composizione nota, nessun mazzo trovato) resta vuoto invece di dire zero.
        var hasValuable = composition.Packs.Count > 0 || composition.Decks.Count > 0 || composition.Cards.Count > 0;
        var cmValue = hasValuable ? opening.Cardmarket.Product(composition) : null;
        var ctValue = hasValuable ? opening.CardTrader.Product(composition) : null;
        decimal? openCm = cmValue is { Coverage: > 0 } ? Math.Round(settings.Net(cmValue.Gross), 2) : null;
        decimal? openCt = ctValue is { Coverage: > 0 } ? Math.Round(settings.Net(ctValue.Gross), 2) : null;

        // Decisione su Cardmarket, dove si compra: aprire conviene se il ricavato netto dalle singole
        // supera quello della rivendita del sigillato (che paga anch'essa i costi di vendita).
        decimal? sealedNet = price?.Trend is > 0 ? Math.Round(settings.Net(price.Trend.Value), 2) : null;
        decimal? openingRoi = openCm is not null && price?.Trend is > 0
            ? Math.Round((openCm.Value - price.Trend.Value) / price.Trend.Value * 100m, 1)
            : null;
        // Con una busta senza composizione o un mazzo non trovato il valore è sottostimato: meglio
        // nessuna decisione che un "tieni sigillato" dovuto ai dati mancanti.
        // Carte dal bordo dorato o non ammesse a torneo (World Championship Deck, Pro Tour Collector
        // Set, Collectors' Edition): nessuna vendita conferma che il trend CM di quelle singole si
        // incassi davvero, e su pochi scambi il trend è poco affidabile.
        string? decision = sealedNet is null || cmValue is null ? null
            : notPlayable ? "Non giocabili"
            : priceMismatch ? "Prezzo CM dubbio"
            : cmValue.MissingPacks.Count > 0 || cmValue.MissingDecks.Count > 0 || cmValue.Coverage < 0.9m ? "Dati incompleti"
            : openCm > sealedNet ? "Apri" : "Tieni sigillato";
        if (priceMismatch) openingRoi = null;

        return new SealedProductAnalysisDto(
            product.Id, product.Name, product.Category, product.Subtype, product.SetCode,
            DescribeContents(product, composition, packs),
            packs, totalPacks,
            composition.HasDeck || composition.HasCards, composition.HasExtras, composition.Unresolved,
            IsCase(product.Category),
            product.CardmarketId, cardmarketName, product.CardTraderBlueprintId,
            price?.Trend, price?.Low, price?.Date,
            product.CtMinPrice, product.CtOfferCount, product.CtPriceUpdatedAt,
            pricePerPack, packValue, deltaPercent,
            openCm, cmValue is null ? null : Math.Round(cmValue.Coverage * 100m, 1),
            openCt, ctValue is null ? null : Math.Round(ctValue.Coverage * 100m, 1),
            sealedNet, openingRoi, decision, priceMismatch, componentsTrend,
            (cmValue?.MissingPacks ?? new()).Select(k => PackLabel(k, mainSetCode)).ToList(),
            cmValue?.MissingDecks ?? new());
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

    /// <summary>Mazzi a contenuto fisso per chiave "SET:nome" (vedi <see cref="OpeningValueCalculator.DeckKey"/>).</summary>
    public Dictionary<string, int> Decks { get; } = new();

    /// <summary>Carte specifiche contenute nel prodotto.</summary>
    public Dictionary<(Guid Uuid, bool Foil), int> Cards { get; } = new();

    public bool HasDeck { get; set; }
    public bool HasCards { get; set; }
    public bool HasExtras { get; set; }
    public bool Unresolved { get; set; }

    /// <summary>Solo buste (con eventuali extra senza valore di rivendita).</summary>
    public bool IsPurePacks => Packs.Count > 0 && !HasDeck && !HasCards && !Unresolved;

    public void AddPacks(string packKey, int count) =>
        Packs[packKey] = Packs.GetValueOrDefault(packKey) + count;

    public void AddDeck(string deckKey, int count) =>
        Decks[deckKey] = Decks.GetValueOrDefault(deckKey) + count;

    public void AddCard(Guid uuid, bool foil, int count) =>
        Cards[(uuid, foil)] = Cards.GetValueOrDefault((uuid, foil)) + count;

    public void Merge(PackComposition child, int times)
    {
        foreach (var (key, count) in child.Packs) AddPacks(key, count * times);
        foreach (var (key, count) in child.Decks) AddDeck(key, count * times);
        foreach (var (key, count) in child.Cards) AddCard(key.Uuid, key.Foil, count * times);
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
    string? CardmarketName,
    int? CardTraderBlueprintId,
    decimal? CmTrend,
    decimal? CmLow,
    DateOnly? CmPriceDate,
    decimal? CtMinPrice,
    int? CtOfferCount,
    DateTime? CtPriceUpdatedAt,
    decimal? PricePerPack,
    decimal? PackValue,
    decimal? DeltaPercent,
    decimal? OpenValueCm,
    decimal? CoverageCm,
    decimal? OpenValueCt,
    decimal? CoverageCt,
    decimal? SealedNetCm,
    decimal? OpeningRoiPercent,
    string? Decision,
    bool PriceMismatch,
    decimal? ComponentsTrend,
    List<string> MissingPacks,
    List<string> MissingDecks);

public record SealedSetAnalysis(
    string Code,
    string Name,
    DateOnly? ReleaseDate,
    List<string> ChildSets,
    DateOnly? CardmarketPriceDate,
    DateTime? CatalogImportedAt,
    List<PackReferenceDto> References,
    List<SealedProductAnalysisDto> Products,
    DateTime? DetailImportedAt,
    bool HasBoosterData,
    OpeningValueSettingsDto Settings,
    List<PackValueDto> PackValues);

/// <summary>Parametri del valore atteso modificabili dalla pagina; null = configurazione o dato misurato.</summary>
public record OpeningValueOverrides(
    decimal? BulkThreshold,
    decimal? BulkPrice,
    decimal? BulkSellThroughPercent,
    decimal? SellingCostPercent,
    decimal? PriceRealizationPercent = null,
    decimal? CostPerCard = null);

public record OpeningValueSettingsDto(
    decimal BulkThreshold,
    decimal BulkPrice,
    decimal BulkSellThroughPercent,
    decimal SellingCostPercent,
    decimal MeasuredBulkSellThroughPercent,
    bool BulkSellThroughMeasured,
    List<BulkSellThroughDto> BulkSellThroughExpansions,
    decimal? MeasuredCardTraderFeePercent,
    decimal PriceRealizationPercent,
    decimal MeasuredPriceRealizationPercent,
    bool PriceRealizationMeasured,
    int PriceRealizationSampleCopies,
    decimal CostPerCard,
    List<SellThroughBandDto> SellThroughBands);

/// <param name="SharePercent">Quota venduta; 100 se il campione è troppo piccolo (<paramref name="Measured"/> falso).</param>
public record SellThroughBandDto(decimal From, decimal? To, int Sold, int InStock, decimal SharePercent, bool Measured);

public record BulkSellThroughDto(string Name, DateTime ReleaseDate, int Sold, int InStock, decimal SharePercent);

/// <param name="ValueCm">Valore atteso lordo di una busta su prezzi Cardmarket.</param>
/// <param name="NetCm">Lo stesso al netto dei costi di vendita.</param>
public record PackValueDto(
    string PackKey,
    string Label,
    decimal ValueCm,
    decimal NetCm,
    decimal CoverageCm,
    decimal? ValueCt,
    decimal? NetCt,
    decimal CoverageCt,
    List<SheetValueDto> Sheets,
    List<TopCardDto> TopCards);

public record SheetValueDto(string Name, decimal SlotsPerPack, decimal ValuePerSlot, decimal CoveragePercent);

/// <param name="ProbabilityPercent">Probabilità di trovarla in una busta, in percentuale.</param>
/// <param name="ExpectedValue">Contributo al valore atteso della busta.</param>
public record TopCardDto(string Name, string? SetCode, string? Number, bool Foil, decimal Value, decimal ProbabilityPercent, decimal ExpectedValue);

public record OpeningCalculators(
    OpeningValueCalculator Cardmarket,
    OpeningValueCalculator CardTrader,
    IReadOnlyDictionary<Guid, MtgjsonCard> Cards,
    DateTime? CardTraderPricesUpdatedAt);

public record CardTraderSealedRefreshResult(int Products, int ProductsWithOffers, int ApiCalls, int CardPrices);
