using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Registro degli acquisti di prodotti sigillati. Alla registrazione, e di nuovo quando si segna
/// l'apertura, salva la previsione del modello (valore atteso netto per unità con i parametri del
/// momento): è il termine di paragone per tarare il modello sulle vendite reali.
/// </summary>
public class ProductPurchaseService
{
    private readonly ApplicationDbContext _db;
    private readonly SealedProductAnalysisService _analysis;

    public ProductPurchaseService(ApplicationDbContext db, SealedProductAnalysisService analysis)
    {
        _db = db;
        _analysis = analysis;
    }

    public async Task<List<ProductPurchaseDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var purchases = await _db.ProductPurchases.AsNoTracking()
            .Include(p => p.SealedProduct)
            .OrderByDescending(p => p.PurchasedAt ?? DateOnly.FromDateTime(p.CreatedAt))
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        var cards = await CardsPerUnitAsync(purchases.Select(p => p.SealedProductId), cancellationToken);
        var cmNames = await CardmarketNamesAsync(purchases.Select(p => p.SealedProduct?.CardmarketId), cancellationToken);
        var today = await TodayDecisionsAsync(purchases.Where(p => p.OpenedAt == null).Select(p => p.SealedProductId), cancellationToken);
        return purchases.Select(p => Map(p, cards.GetValueOrDefault(p.SealedProductId), CmName(cmNames, p.SealedProduct),
            p.OpenedAt == null ? today.GetValueOrDefault(p.SealedProductId) : null)).ToList();
    }

    /// <summary>
    /// Decisione aprire/tenere chiuso dell'ultima classifica giornaliera, per i prodotti non ancora
    /// aperti: dopo l'apertura non conta più. Si guarda all'ultima settimana, perché un prodotto può
    /// mancare dalla classifica di oggi (listino non letto).
    /// </summary>
    private async Task<Dictionary<int, TodayDecision>> TodayDecisionsAsync(IEnumerable<int> productIds, CancellationToken cancellationToken)
    {
        var ids = productIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, TodayDecision>();
        var since = DateOnly.FromDateTime(DateTime.Today.AddDays(-7));
        var rows = await _db.SealedOpportunities.AsNoTracking()
            .Where(o => ids.Contains(o.SealedProductId) && o.Date >= since)
            .Select(o => new { o.SealedProductId, o.Date, o.Decision, o.OpenValueCm, o.SealedNetCm })
            .ToListAsync(cancellationToken);
        return rows
            .GroupBy(o => o.SealedProductId)
            .Select(g => g.OrderByDescending(o => o.Date).First())
            .ToDictionary(o => o.SealedProductId, o => new TodayDecision(o.Decision, o.OpenValueCm, o.SealedNetCm, o.Date));
    }

    private const int MaxSearchResults = 40;

    /// <summary>
    /// Ricerca nel catalogo sigillati per il registro acquisti: ogni parola cercata deve comparire nel
    /// nome Cardmarket, nel nome MTGJSON, nel codice o nel nome del set. I nomi dei due cataloghi sono
    /// spesso diversi ("Commander: Foundations: Deck Set" contro "Foundations Commander Decks Set of 5"),
    /// e si compra pensando al nome Cardmarket. Le uscite più recenti prima.
    /// </summary>
    public async Task<List<CatalogProductDto>> SearchCatalogAsync(string? query, CancellationToken cancellationToken = default)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0) return new List<CatalogProductDto>();

        var sets = await _db.MtgjsonSets.AsNoTracking()
            .ToDictionaryAsync(s => s.Code, s => (s.Name, s.ReleaseDate), cancellationToken);
        var products = await (
                from p in _db.SealedProducts.AsNoTracking()
                join c in _db.CardmarketProducts.AsNoTracking() on p.CardmarketId equals (int?)c.IdProduct into cm
                from c in cm.DefaultIfEmpty()
                select new { p.Id, p.Name, p.SetCode, p.Category, p.Subtype, CardmarketName = c != null ? c.Name : null })
            .ToListAsync(cancellationToken);

        return products
            .Select(p => (Product: p, Set: sets.GetValueOrDefault(p.SetCode)))
            .Where(x => words.All(w =>
                x.Product.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                || (x.Product.CardmarketName?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)
                || x.Product.SetCode.Equals(w, StringComparison.OrdinalIgnoreCase)
                || (x.Set.Name?.Contains(w, StringComparison.OrdinalIgnoreCase) ?? false)))
            .OrderByDescending(x => x.Set.ReleaseDate)
            .ThenBy(x => x.Product.CardmarketName ?? x.Product.Name)
            .Take(MaxSearchResults)
            .Select(x => new CatalogProductDto(x.Product.Id, x.Product.Name, x.Product.CardmarketName, x.Product.SetCode,
                x.Set.Name, x.Product.Category, x.Product.Subtype))
            .ToList();
    }

    private async Task<Dictionary<int, string>> CardmarketNamesAsync(IEnumerable<int?> cardmarketIds, CancellationToken cancellationToken)
    {
        var ids = cardmarketIds.Where(id => id.HasValue).Select(id => id!.Value).Distinct().ToList();
        return await _db.CardmarketProducts.AsNoTracking()
            .Where(c => ids.Contains(c.IdProduct))
            .ToDictionaryAsync(c => c.IdProduct, c => c.Name, cancellationToken);
    }

    private static string? CmName(Dictionary<int, string> names, SealedProduct? product) =>
        product?.CardmarketId is { } id ? names.GetValueOrDefault(id) : null;

    /// <summary>Tipi di busta più comuni, per le uscite di cui MTGJSON non ha ancora la composizione.</summary>
    private static readonly Dictionary<string, int> TypicalCardsPerPack = new(StringComparer.OrdinalIgnoreCase)
    {
        ["play"] = 14, ["collector"] = 15, ["draft"] = 15, ["set"] = 12, ["jumpstart"] = 20, ["beginner"] = 20
    };

    /// <summary>
    /// Carte contenute in un'unità di ciascun prodotto: buste (carte per busta dalla composizione
    /// MTGJSON, media pesata delle configurazioni), mazzi e carte singole. Assente se il contenuto non
    /// è noto per intero. Le buste senza composizione pubblicata contano le carte tipiche del loro tipo,
    /// e il risultato è segnato come stima.
    /// </summary>
    public async Task<Dictionary<int, CardsPerUnit>> CardsPerUnitAsync(IEnumerable<int> productIds, CancellationToken cancellationToken = default)
    {
        var ids = productIds.Distinct().ToList();
        var result = new Dictionary<int, CardsPerUnit>();
        if (ids.Count == 0) return result;

        var catalog = await _db.SealedProducts.AsNoTracking().Include(p => p.Contents)
            .ToDictionaryAsync(p => p.Uuid, cancellationToken);
        var compositions = catalog.Values.Where(p => ids.Contains(p.Id))
            .ToDictionary(p => p.Id, p => SealedProductAnalysisService.Resolve(p, catalog));

        var packSets = compositions.Values.SelectMany(c => c.Packs.Keys).Select(k => k.Split(':')[0]).Distinct().ToList();
        var configs = await _db.BoosterConfigs.AsNoTracking().Include(c => c.Slots)
            .Where(c => packSets.Contains(c.SetCode))
            .ToListAsync(cancellationToken);
        var cardsPerPack = configs
            .GroupBy(c => $"{c.SetCode}:{c.BoosterType}".ToUpperInvariant())
            .ToDictionary(g => g.Key, g =>
            {
                var total = g.First().TotalWeight;
                return total > 0 ? g.Sum(c => (decimal)c.Weight * c.Slots.Sum(s => s.Count)) / total : 0m;
            });

        var deckSets = compositions.Values.SelectMany(c => c.Decks.Keys).Select(k => k.Split(':')[0]).Distinct().ToList();
        var deckCards = (await _db.MtgjsonDecks.AsNoTracking()
                .Where(d => deckSets.Contains(d.SetCode))
                .Select(d => new { d.SetCode, d.Name, Cards = d.Cards.Sum(c => c.Count) })
                .ToListAsync(cancellationToken))
            .GroupBy(d => OpeningValueCalculator.DeckKey(d.SetCode, d.Name))
            .ToDictionary(g => g.Key, g => g.First().Cards);

        foreach (var (productId, composition) in compositions)
        {
            if (composition.Unresolved) continue;

            decimal cards = 0;
            var estimated = false;
            var known = true;
            foreach (var (packKey, count) in composition.Packs)
            {
                if (cardsPerPack.TryGetValue(packKey.ToUpperInvariant(), out var perPack) && perPack > 0)
                {
                    cards += count * perPack;
                }
                else if (TypicalCardsPerPack.TryGetValue(packKey.Split(':').Last(), out var typical))
                {
                    cards += count * typical;
                    estimated = true;
                }
                else known = false;
            }
            foreach (var (deckKey, count) in composition.Decks)
            {
                if (deckCards.TryGetValue(deckKey, out var perDeck) && perDeck > 0) cards += count * perDeck;
                else known = false;
            }
            cards += composition.Cards.Values.Sum();

            if (known && cards > 0) result[productId] = new CardsPerUnit((int)Math.Round(cards), estimated);
        }

        return result;
    }

    /// <exception cref="ArgumentException">Prodotto inesistente o dati non validi.</exception>
    public async Task<ProductPurchaseDto> SaveAsync(int? id, ProductPurchaseInput input, CancellationToken cancellationToken = default)
    {
        if (input.Quantity <= 0) throw new ArgumentException("La quantità deve essere maggiore di zero");
        if (input.UnitPrice < 0) throw new ArgumentException("Il prezzo non può essere negativo");

        var product = await _db.SealedProducts.FirstOrDefaultAsync(p => p.Id == input.SealedProductId, cancellationToken)
                      ?? throw new ArgumentException($"Prodotto sigillato {input.SealedProductId} inesistente");

        ProductPurchase purchase;
        if (id is { } existingId)
        {
            purchase = await _db.ProductPurchases.FirstOrDefaultAsync(p => p.Id == existingId, cancellationToken)
                       ?? throw new ArgumentException($"Acquisto {existingId} inesistente");
        }
        else
        {
            purchase = new ProductPurchase();
            _db.ProductPurchases.Add(purchase);
        }

        // La previsione conta al momento dell'apertura: si rifà quando l'apertura viene segnata o
        // cambia, quando cambia il prodotto, e finché non è stato possibile calcolarla.
        var refreshPrediction = purchase.Id == 0
            || purchase.SealedProductId != input.SealedProductId
            || purchase.OpenedAt != input.OpenedAt
            || purchase.PredictedOpenValueNet == null;

        Apply(purchase, product, input);

        if (refreshPrediction)
        {
            await PredictAsync(purchase, product, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        purchase.SealedProduct = product;
        var cards = await CardsPerUnitAsync(new[] { product.Id }, cancellationToken);
        var cmNames = await CardmarketNamesAsync(new[] { product.CardmarketId }, cancellationToken);
        return Map(purchase, cards.GetValueOrDefault(product.Id), CmName(cmNames, product));
    }

    private static void Apply(ProductPurchase purchase, SealedProduct product, ProductPurchaseInput input)
    {
        purchase.SealedProductId = product.Id;
        purchase.Quantity = input.Quantity;
        purchase.UnitPrice = input.UnitPrice;
        purchase.Store = Trim(input.Store, 100);
        purchase.Seller = Trim(input.Seller, 100);
        purchase.PurchasedAt = input.PurchasedAt;
        purchase.OpenedAt = input.OpenedAt;
        purchase.Tag = Trim(input.Tag, 100);
        purchase.Notes = Trim(input.Notes, 1000);
        purchase.CostPerCard = input.CostPerCard is > 0 ? Math.Round(input.CostPerCard.Value, 2) : null;
        purchase.HoldForResale = input.HoldForResale;
    }

    /// <summary>
    /// Registra più acquisti insieme, tutti o nessuno: è la "Registra acquisto" delle pagine da cui si
    /// compra (Budget, piano d'acquisto). L'analisi di un'uscita si calcola una volta sola anche se
    /// l'ordine ha più prodotti della stessa.
    /// </summary>
    /// <exception cref="ArgumentException">Elenco vuoto, prodotto inesistente o dati non validi.</exception>
    public async Task<int> SaveManyAsync(IReadOnlyList<ProductPurchaseInput> inputs, CancellationToken cancellationToken = default)
    {
        if (inputs.Count == 0) throw new ArgumentException("Nessun acquisto da registrare");
        foreach (var input in inputs)
        {
            if (input.Quantity <= 0) throw new ArgumentException("La quantità deve essere maggiore di zero");
            if (input.UnitPrice < 0) throw new ArgumentException("Il prezzo non può essere negativo");
        }

        var ids = inputs.Select(i => i.SealedProductId).Distinct().ToList();
        var products = await _db.SealedProducts.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var missing = ids.FirstOrDefault(id => !products.ContainsKey(id));
        if (missing != 0) throw new ArgumentException($"Prodotto sigillato {missing} inesistente");

        var analyses = new Dictionary<string, SealedSetAnalysis?>(StringComparer.OrdinalIgnoreCase);
        foreach (var input in inputs)
        {
            var product = products[input.SealedProductId];
            var purchase = new ProductPurchase();
            Apply(purchase, product, input);
            await PredictAsync(purchase, product, cancellationToken, analyses);
            _db.ProductPurchases.Add(purchase);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return inputs.Count;
    }

    /// <summary>Dati di catalogo dei prodotti indicati (per proporre il Tag nella registrazione da un ordine).</summary>
    public async Task<List<CatalogProductDto>> GetCatalogProductsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default)
    {
        var sets = await _db.MtgjsonSets.AsNoTracking().ToDictionaryAsync(s => s.Code, s => s.Name, cancellationToken);
        var products = await (
                from p in _db.SealedProducts.AsNoTracking().Where(p => ids.Contains(p.Id))
                join c in _db.CardmarketProducts.AsNoTracking() on p.CardmarketId equals (int?)c.IdProduct into cm
                from c in cm.DefaultIfEmpty()
                select new { p.Id, p.Name, p.SetCode, p.Category, p.Subtype, CardmarketName = c != null ? c.Name : null })
            .ToListAsync(cancellationToken);
        return products
            .Select(p => new CatalogProductDto(p.Id, p.Name, p.CardmarketName, p.SetCode, sets.GetValueOrDefault(p.SetCode),
                p.Category, p.Subtype))
            .ToList();
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var purchase = await _db.ProductPurchases.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (purchase == null) return false;
        _db.ProductPurchases.Remove(purchase);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Valore atteso netto per unità, sui prezzi Cardmarket corretti. Solo se il modello dà una
    /// decisione: con dati incompleti la previsione sarebbe sottostimata e resta vuota.
    /// </summary>
    /// <param name="analyses">Analisi già calcolate per uscita, da riusare in una registrazione di più prodotti.</param>
    private async Task PredictAsync(ProductPurchase purchase, SealedProduct product, CancellationToken cancellationToken,
        Dictionary<string, SealedSetAnalysis?>? analyses = null)
    {
        var set = await _db.MtgjsonSets.AsNoTracking().FirstOrDefaultAsync(s => s.Code == product.SetCode, cancellationToken);
        var mainCode = set?.ParentCode ?? product.SetCode;

        if (analyses == null || !analyses.TryGetValue(mainCode, out var analysis))
        {
            analysis = await _analysis.AnalyzeAsync(mainCode, null, cancellationToken);
            analyses?.Add(mainCode, analysis);
        }
        var row = analysis?.Products.FirstOrDefault(p => p.Id == product.Id);

        var reliable = row is { OpenValueCm: not null } && row.Decision is "Apri" or "Tieni sigillato";
        purchase.PredictedOpenValueNet = reliable ? row!.OpenValueCm : null;
        purchase.PredictionCoverage = reliable ? row!.CoverageCm : null;
        purchase.PredictedAt = reliable ? DateTime.UtcNow : null;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    /// <summary>
    /// Il costo per carta da usare è quello scritto a mano, altrimenti il prezzo di un'unità diviso le
    /// carte che contiene: al 09/10/2026 è il modo in cui l'utente calcolava a mano il prezzo d'acquisto
    /// delle inserzioni (es. Collector Box = prezzo / 12 buste × 15 carte).
    /// </summary>
    private static ProductPurchaseDto Map(ProductPurchase p, CardsPerUnit? cards, string? cardmarketName, TodayDecision? today = null)
    {
        decimal? calculated = cards is { Cards: > 0 } ? Math.Round(p.UnitPrice / cards.Cards, 2) : null;
        return new ProductPurchaseDto(
            p.Id, p.SealedProductId, p.SealedProduct?.Name ?? string.Empty, p.SealedProduct?.SetCode ?? string.Empty,
            p.Quantity, p.UnitPrice, Math.Round(p.Quantity * p.UnitPrice, 2),
            p.Store, p.Seller, p.PurchasedAt, p.OpenedAt, p.Tag, p.Notes,
            p.PredictedOpenValueNet, p.PredictionCoverage, p.PredictedAt,
            p.PredictedOpenValueNet.HasValue ? Math.Round(p.Quantity * p.PredictedOpenValueNet.Value, 2) : null,
            p.CostPerCard, calculated, cards?.Cards, cards?.Estimated ?? false,
            p.CostPerCard ?? calculated, cardmarketName, p.HoldForResale,
            today?.Decision, today?.OpenValueNet, today?.SealedNet, today?.Date);
    }
}

/// <param name="OpenValueNet">Valore atteso netto aprendolo, per unità.</param>
/// <param name="SealedNet">Ricavato netto rivendendolo chiuso al trend, per unità.</param>
public record TodayDecision(string? Decision, decimal? OpenValueNet, decimal? SealedNet, DateOnly Date);

public record ProductPurchaseInput(
    int SealedProductId,
    int Quantity,
    decimal UnitPrice,
    string? Store,
    string? Seller,
    DateOnly? PurchasedAt,
    DateOnly? OpenedAt,
    string? Tag,
    string? Notes,
    decimal? CostPerCard = null,
    bool HoldForResale = false);

/// <param name="Cards">Carte contenute in un'unità del prodotto.</param>
/// <param name="Estimated">True se alcune buste non hanno la composizione MTGJSON e contano le carte tipiche del tipo.</param>
public record CardsPerUnit(int Cards, bool Estimated);

/// <param name="CostPerCard">Costo per carta scritto a mano; null se si usa quello calcolato.</param>
/// <param name="CalculatedCostPerCard">Prezzo di un'unità diviso le carte che contiene.</param>
/// <param name="EffectiveCostPerCard">Quello da usare nelle inserzioni: scritto a mano, altrimenti calcolato.</param>
/// <param name="TodayDecision">Decisione dell'ultima classifica (Apri / Tieni sigillato / ...), solo per i non aperti.</param>
public record ProductPurchaseDto(
    int Id,
    int SealedProductId,
    string ProductName,
    string SetCode,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    string? Store,
    string? Seller,
    DateOnly? PurchasedAt,
    DateOnly? OpenedAt,
    string? Tag,
    string? Notes,
    decimal? PredictedOpenValueNet,
    decimal? PredictionCoverage,
    DateTime? PredictedAt,
    decimal? PredictedTotalNet,
    decimal? CostPerCard,
    decimal? CalculatedCostPerCard,
    int? CardsPerUnit,
    bool CardsEstimated,
    decimal? EffectiveCostPerCard,
    string? CardmarketName,
    bool HoldForResale,
    string? TodayDecision = null,
    decimal? TodayOpenValueNet = null,
    decimal? TodaySealedNet = null,
    DateOnly? TodayDecisionDate = null);

/// <param name="Name">Nome MTGJSON (quello del catalogo).</param>
/// <param name="CardmarketName">Nome dello stesso prodotto su Cardmarket, se abbinato.</param>
public record CatalogProductDto(int Id, string Name, string? CardmarketName, string SetCode, string? SetName, string? Category, string? Subtype);
