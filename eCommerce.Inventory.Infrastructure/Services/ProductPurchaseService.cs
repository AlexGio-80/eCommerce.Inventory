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
        return purchases.Select(p => Map(p, cards.GetValueOrDefault(p.SealedProductId))).ToList();
    }

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

        if (refreshPrediction)
        {
            await PredictAsync(purchase, product, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        purchase.SealedProduct = product;
        var cards = await CardsPerUnitAsync(new[] { product.Id }, cancellationToken);
        return Map(purchase, cards.GetValueOrDefault(product.Id));
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
    private async Task PredictAsync(ProductPurchase purchase, SealedProduct product, CancellationToken cancellationToken)
    {
        var set = await _db.MtgjsonSets.AsNoTracking().FirstOrDefaultAsync(s => s.Code == product.SetCode, cancellationToken);
        var mainCode = set?.ParentCode ?? product.SetCode;

        var analysis = await _analysis.AnalyzeAsync(mainCode, null, cancellationToken);
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
    private static ProductPurchaseDto Map(ProductPurchase p, CardsPerUnit? cards)
    {
        decimal? calculated = cards is { Cards: > 0 } ? Math.Round(p.UnitPrice / cards.Cards, 2) : null;
        return new ProductPurchaseDto(
            p.Id, p.SealedProductId, p.SealedProduct?.Name ?? string.Empty, p.SealedProduct?.SetCode ?? string.Empty,
            p.Quantity, p.UnitPrice, Math.Round(p.Quantity * p.UnitPrice, 2),
            p.Store, p.Seller, p.PurchasedAt, p.OpenedAt, p.Tag, p.Notes,
            p.PredictedOpenValueNet, p.PredictionCoverage, p.PredictedAt,
            p.PredictedOpenValueNet.HasValue ? Math.Round(p.Quantity * p.PredictedOpenValueNet.Value, 2) : null,
            p.CostPerCard, calculated, cards?.Cards, cards?.Estimated ?? false,
            p.CostPerCard ?? calculated);
    }
}

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
    decimal? CostPerCard = null);

/// <param name="Cards">Carte contenute in un'unità del prodotto.</param>
/// <param name="Estimated">True se alcune buste non hanno la composizione MTGJSON e contano le carte tipiche del tipo.</param>
public record CardsPerUnit(int Cards, bool Estimated);

/// <param name="CostPerCard">Costo per carta scritto a mano; null se si usa quello calcolato.</param>
/// <param name="CalculatedCostPerCard">Prezzo di un'unità diviso le carte che contiene.</param>
/// <param name="EffectiveCostPerCard">Quello da usare nelle inserzioni: scritto a mano, altrimenti calcolato.</param>
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
    decimal? EffectiveCostPerCard);
