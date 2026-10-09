using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Quanta parte del bulk messo in vendita si vende davvero, ricavata dalle vendite reali.
///
/// Si considerano solo le espansioni aperte all'uscita (prima carta messa in vendita fra 15 giorni
/// prima e 45 dopo la data di uscita): è il caso di chi compra box di un'uscita nuova. Le espansioni
/// vecchie, comprate come collezioni, vendono il bulk molto meno (5-15% contro 25-50%) e falserebbero
/// la stima.
///
/// È un'approssimazione: "bulk venduto" sono le copie vendute a prezzo non superiore alla soglia,
/// "bulk in vendita" quelle oggi esposte sotto la soglia. Una carta passata di fascia nel tempo
/// finisce dall'una o dall'altra parte a seconda del prezzo del momento.
/// </summary>
public class BulkSellThroughService
{
    /// <summary>Sotto questo numero di copie un'espansione dice poco e viene ignorata.</summary>
    private const int MinCopies = 500;

    /// <summary>Usato se non c'è nessuna apertura all'uscita da cui misurare.</summary>
    public const decimal Fallback = 0.30m;

    private readonly ApplicationDbContext _db;

    public BulkSellThroughService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<BulkSellThrough> MeasureAsync(decimal threshold, CancellationToken cancellationToken = default)
    {
        // Solo ordini pagati: gli ordini "hub_pending" di Card Trader Zero sono i singoli acquisti che
        // Card Trader poi raccoglie in un ordine "Ct connect" pagato, e contarli venderebbe due volte.
        var sold = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Price <= threshold && oi.Blueprint != null && oi.Order.PaidAt != null)
            .GroupBy(oi => oi.Blueprint!.ExpansionId)
            .Select(g => new { ExpansionId = g.Key, Quantity = g.Sum(oi => oi.Quantity) })
            .ToDictionaryAsync(x => x.ExpansionId, x => x.Quantity, cancellationToken);

        var stock = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.ListingPrice <= threshold)
            .GroupBy(i => i.Blueprint.ExpansionId)
            .Select(g => new { ExpansionId = g.Key, Quantity = g.Sum(i => i.Quantity), FirstAdded = g.Min(i => i.DateAdded) })
            .ToDictionaryAsync(x => x.ExpansionId, x => x, cancellationToken);

        var expansionIds = stock.Keys.ToList();
        var expansions = await _db.Expansions.AsNoTracking()
            .Where(e => expansionIds.Contains(e.Id) && e.ReleaseDate != null)
            .Select(e => new { e.Id, e.Name, ReleaseDate = e.ReleaseDate!.Value })
            .ToListAsync(cancellationToken);

        var opened = expansions
            .Select(e =>
            {
                var s = stock[e.Id];
                var soldQty = sold.GetValueOrDefault(e.Id);
                return new BulkSellThroughExpansion(e.Name, e.ReleaseDate, soldQty, s.Quantity, s.FirstAdded, e.Id);
            })
            .Where(x => x.FirstListed >= x.ReleaseDate.AddDays(-15) && x.FirstListed <= x.ReleaseDate.AddDays(45))
            .Where(x => x.Sold + x.InStock >= MinCopies)
            .OrderByDescending(x => x.ReleaseDate)
            .ToList();

        var totalSold = opened.Sum(x => x.Sold);
        var total = opened.Sum(x => x.Sold + x.InStock);

        return new BulkSellThrough(
            total > 0 ? Math.Round((decimal)totalSold / total, 3) : Fallback,
            total > 0,
            opened);
    }

    /// <summary>Estremi inferiori delle fasce sopra la soglia del bulk.</summary>
    public static readonly decimal[] BandStarts = { 1m, 3m, 10m };

    /// <summary>Sotto questo numero di copie una fascia dice poco: si presume di vendere tutto.</summary>
    private const int MinBandCopies = 100;

    /// <summary>
    /// Quota venduta per fascia di prezzo sopra la soglia del bulk, sulle stesse aperture all'uscita
    /// del bulk. Al 09/10/2026, contando solo gli ordini pagati: 65% fra 0,25 e 1 €, 97-100% sopra
    /// (prima del filtro, con i doppioni di Card Trader Zero: 78% e 95-98%).
    ///
    /// Stessa approssimazione del bulk: le vendite si contano al prezzo di vendita, la giacenza al
    /// prezzo di oggi. Le fasce poi si applicano al trend Cardmarket, non al prezzo Card Trader.
    /// </summary>
    public async Task<List<SellThroughBandMeasure>> MeasureBandsAsync(
        decimal threshold, BulkSellThrough bulk, CancellationToken cancellationToken = default)
    {
        var starts = new[] { threshold }.Concat(BandStarts.Where(s => s > threshold)).ToList();
        var ids = bulk.Expansions.Select(e => e.ExpansionId).ToList();

        var soldRows = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Price > threshold && oi.Blueprint != null && oi.Order.PaidAt != null && ids.Contains(oi.Blueprint.ExpansionId))
            .Select(oi => new { oi.Price, oi.Quantity })
            .ToListAsync(cancellationToken);

        var stockRows = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.ListingPrice > threshold && ids.Contains(i.Blueprint.ExpansionId))
            .Select(i => new { Price = i.ListingPrice, i.Quantity })
            .ToListAsync(cancellationToken);

        int BandOf(decimal price) => starts.FindLastIndex(s => price >= s);

        return starts.Select((from, index) =>
        {
            var sold = soldRows.Where(r => BandOf(r.Price) == index).Sum(r => r.Quantity);
            var inStock = stockRows.Where(r => BandOf(r.Price) == index).Sum(r => r.Quantity);
            var measured = sold + inStock >= MinBandCopies;
            var to = index + 1 < starts.Count ? starts[index + 1] : (decimal?)null;
            return new SellThroughBandMeasure(from, to, sold, inStock,
                measured ? Math.Round((decimal)sold / (sold + inStock), 3) : 1m, measured);
        }).ToList();
    }
}

/// <param name="To">Estremo superiore escluso; null = nessun limite.</param>
/// <param name="Share">Quota venduta (0-1); 1 se il campione è troppo piccolo.</param>
public record SellThroughBandMeasure(decimal From, decimal? To, int Sold, int InStock, decimal Share, bool Measured);

/// <param name="Share">Quota venduta (0-1).</param>
/// <param name="Measured">False se non c'erano dati e si usa il valore di ripiego.</param>
/// <param name="Expansions">Le aperture all'uscita su cui si è misurato.</param>
public record BulkSellThrough(decimal Share, bool Measured, List<BulkSellThroughExpansion> Expansions);

public record BulkSellThroughExpansion(string Name, DateTime ReleaseDate, int Sold, int InStock, DateTime FirstListed, int ExpansionId = 0)
{
    public decimal Share => Sold + InStock > 0 ? Math.Round((decimal)Sold / (Sold + InStock), 3) : 0;
}
