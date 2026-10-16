using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Piano d'acquisto su Card Trader per i prodotti convenienti della classifica: le offerte reali
/// dei venditori, raggruppate per venditore, per comprare più prodotti in un'unica spedizione.
///
/// Cardmarket non è utilizzabile: il listino pubblico ha solo prezzi aggregati, e le offerte dei
/// venditori si vedrebbero solo con l'API (chiusa alle nuove richieste) o leggendo le pagine.
/// Card Trader invece dà le offerte per venditore, e con Card Trader Zero articoli di venditori
/// diversi arrivano in un'unica spedizione: per quelli conta il totale combinato.
///
/// Calcolato solo a richiesta: le offerte cambiano di ora in ora. Una chiamata al marketplace per
/// prodotto, con il limitatore condiviso delle 20 richieste al minuto.
/// </summary>
public class PurchasePlanService
{
    /// <summary>Oltre questo numero di prodotti il piano richiederebbe troppi minuti di chiamate.</summary>
    public const int MaxProducts = 40;

    private readonly ApplicationDbContext _db;
    private readonly ICardTraderApiService _cardTrader;
    private readonly ILogger<PurchasePlanService> _logger;

    public PurchasePlanService(ApplicationDbContext db, ICardTraderApiService cardTrader, ILogger<PurchasePlanService> logger)
    {
        _db = db;
        _cardTrader = cardTrader;
        _logger = logger;
    }

    /// <exception cref="ArgumentException">Nessun prodotto o troppi prodotti.</exception>
    public async Task<PurchasePlan> BuildAsync(IReadOnlyCollection<int> sealedProductIds, CancellationToken cancellationToken = default)
    {
        if (sealedProductIds.Count == 0) throw new ArgumentException("Nessun prodotto per il piano d'acquisto");
        if (sealedProductIds.Count > MaxProducts)
            throw new ArgumentException($"Troppi prodotti ({sealedProductIds.Count}): al massimo {MaxProducts}, restringi i filtri della classifica");

        var ids = sealedProductIds.Distinct().ToList();
        var lastDate = await _db.SealedOpportunities.Select(o => (DateOnly?)o.Date).MaxAsync(cancellationToken);

        var products = await _db.SealedProducts.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id, p.Name, p.CardTraderBlueprintId,
                // Per cercare il prodotto su Cardmarket e confrontarne il prezzo.
                CardmarketName = _db.CardmarketProducts.Where(c => c.IdProduct == p.CardmarketId).Select(c => c.Name).FirstOrDefault(),
                Opportunity = _db.SealedOpportunities
                    .Where(o => o.SealedProductId == p.Id && o.Date == lastDate)
                    .Select(o => new { o.OpenValueCm, o.CmTrend, o.MainSetCode })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        var offers = new List<PlanOffer>();
        var productSummaries = new List<PlanProduct>();

        foreach (var product in products)
        {
            var openValue = product.Opportunity?.OpenValueCm;
            if (product.CardTraderBlueprintId is not { } blueprintId || openValue is not > 0)
            {
                productSummaries.Add(new PlanProduct(product.Id, product.Name, product.Opportunity?.MainSetCode, openValue,
                    product.Opportunity?.CmTrend, 0, null, null, CardTraderBlueprintId: product.CardTraderBlueprintId,
                    CardmarketName: product.CardmarketName, Note: product.CardTraderBlueprintId == null ? "Nessun blueprint Card Trader abbinato" : "Nessun valore atteso"));
                continue;
            }

            var marketplace = await _cardTrader.GetMarketplaceProductsAsync(blueprintId, cancellationToken);
            var valid = marketplace
                .Where(o => o.BlueprintId == blueprintId && o.PriceCents > 0 && o.Quantity > 0 && !o.OnVacation)
                .Where(o => IsEnglish(o.Properties.Language))
                .Select(o => new PlanOffer(
                    product.Id, product.Name, product.Opportunity!.MainSetCode,
                    o.User.Id, o.User.Username, o.User.CountryCode, o.User.CanSellSealedWithCtZero,
                    o.PriceCents / 100m, o.Quantity, openValue.Value,
                    Math.Round((openValue.Value - o.PriceCents / 100m) / (o.PriceCents / 100m) * 100m, 1),
                    o.Id))
                .ToList();

            var cheapest = valid.OrderBy(o => o.Price).FirstOrDefault();
            productSummaries.Add(new PlanProduct(product.Id, product.Name, product.Opportunity!.MainSetCode, openValue,
                product.Opportunity.CmTrend, valid.Count, cheapest?.Price, cheapest?.SellerName, blueprintId,
                product.CardmarketName,
                valid.Count == 0 ? "Nessuna offerta in inglese su Card Trader" : null));

            // Nel piano entrano solo le offerte a cui aprire il prodotto conviene.
            offers.AddRange(valid.Where(o => o.RoiPercent > 0));
        }

        // Per venditore: per ogni prodotto la sua offerta più economica.
        var sellers = offers
            .GroupBy(o => o.SellerId)
            .Select(g =>
            {
                var items = g.GroupBy(o => o.ProductId).Select(p => p.OrderBy(o => o.Price).First()).OrderByDescending(o => o.RoiPercent).ToList();
                var total = items.Sum(o => o.Price);
                var value = items.Sum(o => o.OpenValueNet);
                return new PlanSeller(g.Key, items[0].SellerName, items[0].SellerCountry, items[0].CtZero,
                    items.Count, Math.Round(total, 2), Math.Round(value, 2), Math.Round(value - total, 2), items);
            })
            .OrderByDescending(s => s.ProductCount)
            .ThenByDescending(s => s.Margin)
            .ToList();

        // Card Trader Zero: un'unica spedizione anche fra venditori diversi, quindi per ogni prodotto
        // basta l'offerta CT Zero più economica.
        var zeroItems = offers
            .Where(o => o.CtZero)
            .GroupBy(o => o.ProductId)
            .Select(g => g.OrderBy(o => o.Price).First())
            .OrderByDescending(o => o.RoiPercent)
            .ToList();
        var zero = new PlanZeroBasket(zeroItems.Count, Math.Round(zeroItems.Sum(o => o.Price), 2),
            Math.Round(zeroItems.Sum(o => o.OpenValueNet), 2), Math.Round(zeroItems.Sum(o => o.OpenValueNet - o.Price), 2), zeroItems);

        _logger.LogInformation(
            "Piano d'acquisto Card Trader: {Products} prodotti, {Offers} offerte convenienti, {Sellers} venditori, {Zero} prodotti in CT Zero",
            products.Count, offers.Count, sellers.Count, zeroItems.Count);

        return new PurchasePlan(DateTime.UtcNow, productSummaries.OrderBy(p => p.Name).ToList(), sellers, zero);
    }

    /// <summary>Offerte aggiungibili al carrello con un clic: oltre, meglio restringere il piano.</summary>
    public const int MaxCartItems = 60;

    /// <summary>
    /// Mette nel carrello Card Trader dell'account le offerte scelte nel piano, una unità ciascuna
    /// (via Card Trader Zero per il carrello CT Zero, direttamente dal venditore per gli altri).
    /// Non compra: l'acquisto si conclude sul sito. Un'offerta nel frattempo venduta o modificata
    /// viene rifiutata da Card Trader e finisce fra quelle non aggiunte, senza fermare le altre.
    ///
    /// Le offerte già nel carrello si saltano: Card Trader ne alzerebbe la quantità (prima prova del
    /// 16/10: un prodotto del carrello CT Zero e dello stesso venditore finiva nel carrello due volte).
    /// </summary>
    /// <exception cref="ArgumentException">Nessuna offerta o troppe offerte.</exception>
    public async Task<PlanCartResult> AddToCartAsync(IReadOnlyCollection<PlanCartItem> items, CancellationToken cancellationToken = default)
    {
        if (items.Count == 0) throw new ArgumentException("Nessuna offerta da mettere nel carrello");
        if (items.Count > MaxCartItems) throw new ArgumentException($"Troppe offerte ({items.Count}): al massimo {MaxCartItems} per volta");

        var inCart = (await _cardTrader.GetCartAsync(cancellationToken)).Select(l => l.ProductId).ToHashSet();
        var added = 0;
        var alreadyInCart = 0;
        var failed = new List<PlanCartFailure>();
        foreach (var item in items.GroupBy(i => i.CardTraderProductId).Select(g => g.First()))
        {
            if (inCart.Contains(item.CardTraderProductId)) { alreadyInCart++; continue; }
            var error = await _cardTrader.AddToCartAsync(item.CardTraderProductId, 1, item.ViaCardTraderZero, cancellationToken);
            if (error == null) added++;
            else failed.Add(new PlanCartFailure(item.CardTraderProductId, item.ProductName, error));
        }

        _logger.LogInformation("Carrello Card Trader dal piano: {Added} offerte aggiunte, {Already} già nel carrello, {Failed} rifiutate",
            added, alreadyInCart, failed.Count);
        return new PlanCartResult(added, alreadyInCart, failed, await GetCartAsync(cancellationToken));
    }

    /// <summary>Contenuto del carrello Card Trader, per segnare nel piano le offerte già messe.</summary>
    public async Task<List<CardTraderCartLine>> GetCartAsync(CancellationToken cancellationToken = default) =>
        await _cardTrader.GetCartAsync(cancellationToken);

    /// <summary>Toglie dal carrello Card Trader le offerte indicate, tutte le unità presenti.</summary>
    public async Task<PlanCartResult> RemoveFromCartAsync(IReadOnlyCollection<int> cardTraderProductIds, CancellationToken cancellationToken = default)
    {
        if (cardTraderProductIds.Count == 0) throw new ArgumentException("Nessuna offerta da togliere dal carrello");

        var cart = (await _cardTrader.GetCartAsync(cancellationToken)).GroupBy(l => l.ProductId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
        var failed = new List<PlanCartFailure>();
        foreach (var id in cardTraderProductIds.Distinct())
        {
            if (!cart.TryGetValue(id, out var quantity)) continue;
            var error = await _cardTrader.RemoveFromCartAsync(id, quantity, cancellationToken);
            if (error != null) failed.Add(new PlanCartFailure(id, null, error));
        }
        return new PlanCartResult(0, 0, failed, await GetCartAsync(cancellationToken));
    }

    /// <summary>Si compra solo in inglese; un'offerta senza lingua indicata è accettata (sigillati).</summary>
    private static bool IsEnglish(string? language) =>
        string.IsNullOrEmpty(language)
        || string.Equals(language, "en", StringComparison.OrdinalIgnoreCase)
        || string.Equals(language, "English", StringComparison.OrdinalIgnoreCase);
}

/// <param name="OpenValueNet">Valore atteso netto dell'apertura (dalla classifica).</param>
/// <param name="RoiPercent">Resa dell'apertura al prezzo di questa offerta.</param>
/// <param name="CardTraderProductId">Id dell'offerta sul marketplace, per metterla nel carrello.</param>
public record PlanOffer(
    int ProductId,
    string ProductName,
    string SetCode,
    int SellerId,
    string SellerName,
    string? SellerCountry,
    bool CtZero,
    decimal Price,
    int Available,
    decimal OpenValueNet,
    decimal RoiPercent,
    int CardTraderProductId);

public record PlanProduct(
    int ProductId,
    string Name,
    string? SetCode,
    decimal? OpenValueNet,
    decimal? CmTrend,
    int OfferCount,
    decimal? CheapestPrice,
    string? CheapestSeller,
    int? CardTraderBlueprintId,
    string? CardmarketName,
    string? Note);

/// <param name="Margin">Valore atteso netto meno prezzo, per un'unità di ciascun prodotto.</param>
public record PlanSeller(
    int SellerId,
    string SellerName,
    string? Country,
    bool CtZero,
    int ProductCount,
    decimal Total,
    decimal OpenValueNet,
    decimal Margin,
    List<PlanOffer> Items);

public record PlanZeroBasket(int ProductCount, decimal Total, decimal OpenValueNet, decimal Margin, List<PlanOffer> Items);

public record PurchasePlan(DateTime ComputedAt, List<PlanProduct> Products, List<PlanSeller> Sellers, PlanZeroBasket CtZero);

/// <summary>Offerta del piano da mettere nel carrello Card Trader.</summary>
/// <param name="ViaCardTraderZero">True per le offerte del carrello CT Zero (un'unica spedizione).</param>
public record PlanCartItem(int CardTraderProductId, bool ViaCardTraderZero, string? ProductName);

public record PlanCartFailure(int CardTraderProductId, string? ProductName, string Reason);

/// <param name="Cart">Carrello Card Trader dopo l'operazione.</param>
public record PlanCartResult(int Added, int AlreadyInCart, List<PlanCartFailure> Failed, List<CardTraderCartLine> Cart);
