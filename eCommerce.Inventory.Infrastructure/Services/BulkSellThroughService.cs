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
        var sold = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Price <= threshold && oi.Blueprint != null)
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
                return new BulkSellThroughExpansion(e.Name, e.ReleaseDate, soldQty, s.Quantity, s.FirstAdded);
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
}

/// <param name="Share">Quota venduta (0-1).</param>
/// <param name="Measured">False se non c'erano dati e si usa il valore di ripiego.</param>
public record BulkSellThrough(decimal Share, bool Measured, List<BulkSellThroughExpansion> Expansions);

public record BulkSellThroughExpansion(string Name, DateTime ReleaseDate, int Sold, int InStock, DateTime FirstListed)
{
    public decimal Share => Sold + InStock > 0 ? Math.Round((decimal)Sold / (Sold + InStock), 3) : 0;
}
