using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Costo d'acquisto delle carte caricate dalla maschera "Nuovo Prodotto", per espansione e per tag.
/// Unica fonte per il report di redditività, la pagina Espansioni e il bilancio delle aperture.
///
/// Il costo è <c>PurchasePrice × copie</c> di ogni caricamento, ma una modifica fatta dalla maschera
/// (<c>IsUpdate</c>) porta la nuova quantità totale dell'inserzione, non le copie aggiunte: contarla per
/// intero riconterebbe tutte le copie già presenti, con il loro costo. Fino al 2026-10-07 la vista
/// <c>ExpansionsROI</c> e il report per tag lo facevano, e per Marvel Super Heroes il costo risultava
/// 3.674 € invece di circa 2.040 €.
/// </summary>
public class PurchaseCostService
{
    private readonly ApplicationDbContext _db;

    public PurchaseCostService(ApplicationDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Caricamenti con le copie davvero aggiunte. Senza <paramref name="tag"/> tutti, altrimenti solo
    /// quelli di quel tag (confronto esatto, come nel report per tag).
    /// </summary>
    public async Task<List<PurchaseCostRow>> GetRowsAsync(string? tag = null, CancellationToken cancellationToken = default)
    {
        var query = _db.PendingListings.AsNoTracking();
        if (tag != null) query = query.Where(pl => pl.Tag == tag);

        // Join espliciti e non navigation properties, come nel report per tag: un caricamento con un
        // blueprint senza espansione resta escluso in modo esplicito e uguale ovunque.
        var rows = await (
                from pl in query
                join bp in _db.Blueprints.AsNoTracking() on pl.BlueprintId equals bp.Id
                join ex in _db.Expansions.AsNoTracking() on bp.ExpansionId equals ex.Id
                select new UploadRow(pl.Id, pl.Tag, pl.Quantity, pl.PurchasePrice, pl.CreatedAt, pl.IsUpdate,
                    pl.CardTraderProductId, ex.Name, ex.ReleaseDate))
            .ToListAsync(cancellationToken);

        var added = await AddedCopiesAsync(rows, cancellationToken);

        return rows
            .Select(r => new PurchaseCostRow(r.Id, r.Tag, r.ExpansionName, r.ReleaseDate, r.CreatedAt,
                r.PurchasePrice, r.Quantity, added[r.Id], r.PurchasePrice * added[r.Id]))
            .ToList();
    }

    /// <summary>
    /// Costo per espansione, per nome come la vista <c>ExpansionsROI</c>. Il confronto ignora le
    /// maiuscole come SQL Server: il nome dell'espansione e quello riportato sugli ordini possono
    /// differire (es. "Shadows Over Innistrad" e "Shadows over Innistrad").
    /// </summary>
    public async Task<Dictionary<string, decimal>> CostByExpansionAsync(CancellationToken cancellationToken = default) =>
        (await GetRowsAsync(null, cancellationToken))
            .GroupBy(r => r.ExpansionName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Cost), StringComparer.OrdinalIgnoreCase);

    /// <summary>Costo per tag; anche qui il confronto ignora le maiuscole, come in SQL.</summary>
    public async Task<Dictionary<string, decimal>> CostByTagAsync(CancellationToken cancellationToken = default) =>
        (await GetRowsAsync(null, cancellationToken))
            .Where(r => !string.IsNullOrEmpty(r.Tag))
            .GroupBy(r => r.Tag!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Cost), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Copie davvero aggiunte da ogni caricamento. Il primo caricamento di un'inserzione conta per
    /// intero; una modifica successiva conta la differenza con la quantità che l'inserzione aveva
    /// prima, presa dallo storico prezzi (che registra anche la quantità, dal 2026-08-29) o, in
    /// mancanza, dal caricamento precedente della stessa inserzione, anche se di un altro tag. Con
    /// vendite avvenute nel frattempo la stima è leggermente per difetto.
    /// </summary>
    private async Task<Dictionary<int, int>> AddedCopiesAsync(List<UploadRow> rows, CancellationToken cancellationToken)
    {
        var result = rows.ToDictionary(r => r.Id, r => r.Quantity);
        var updates = rows.Where(r => r.IsUpdate && r.CardTraderProductId.HasValue).ToList();
        if (updates.Count == 0) return result;

        var productIds = updates.Select(u => u.CardTraderProductId!.Value).Distinct().ToList();

        var history = (await _db.PriceHistoryEntries.AsNoTracking()
                .Where(h => productIds.Contains(h.CardTraderProductId))
                .Select(h => new { h.CardTraderProductId, h.RecordedAt, h.Quantity })
                .ToListAsync(cancellationToken))
            .GroupBy(h => h.CardTraderProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(h => h.RecordedAt).ToList());

        var previousUploads = (await _db.PendingListings.AsNoTracking()
                .Where(pl => pl.CardTraderProductId != null && productIds.Contains(pl.CardTraderProductId.Value))
                .Select(pl => new { pl.Id, ProductId = pl.CardTraderProductId!.Value, pl.CreatedAt, pl.Quantity })
                .ToListAsync(cancellationToken))
            .GroupBy(pl => pl.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderBy(pl => pl.CreatedAt).ToList());

        foreach (var update in updates)
        {
            var productId = update.CardTraderProductId!.Value;

            // Il primo caricamento di un'inserzione conta per intero anche se è una modifica: vuol dire
            // che l'inserzione era nata direttamente su Card Trader (o con il sistema precedente), e
            // questo è l'unico punto in cui il suo costo entra. Lo storico sa già quante copie c'erano,
            // e senza questa eccezione il costo di quelle carte sparirebbe (trovato su Shadows over
            // Innistrad: 102,80 € diventati zero).
            var earlier = previousUploads.TryGetValue(productId, out var previous)
                ? previous.LastOrDefault(r => r.CreatedAt < update.CreatedAt && r.Id != update.Id)
                : null;
            if (earlier == null) continue;

            int? before = history.TryGetValue(productId, out var points)
                ? points.LastOrDefault(h => h.RecordedAt < update.CreatedAt)?.Quantity
                : null;
            before ??= earlier.Quantity;

            result[update.Id] = before is { } quantityBefore ? Math.Max(0, update.Quantity - quantityBefore) : update.Quantity;
        }

        return result;
    }

    private record UploadRow(int Id, string? Tag, int Quantity, decimal PurchasePrice, DateTime CreatedAt,
        bool IsUpdate, int? CardTraderProductId, string ExpansionName, DateTime? ReleaseDate);
}

/// <param name="Quantity">Quantità del caricamento (per una modifica, il nuovo totale dell'inserzione).</param>
/// <param name="AddedCopies">Copie davvero aggiunte: è su queste che si conta il costo.</param>
public record PurchaseCostRow(
    int Id,
    string? Tag,
    string ExpansionName,
    DateTime? ReleaseDate,
    DateTime CreatedAt,
    decimal PurchasePrice,
    int Quantity,
    int AddedCopies,
    decimal Cost);
