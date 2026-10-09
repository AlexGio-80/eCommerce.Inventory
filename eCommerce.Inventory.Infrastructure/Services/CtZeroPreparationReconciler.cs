using eCommerce.Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Preparazione delle vendite Card Trader Zero.
///
/// Ogni vendita CT Zero arriva prima come ordine "hub_pending" singolo (acquirente "Ct connect", senza
/// pagamento), e le carte si preparano giorno per giorno da "Articoli da preparare". Poi Card Trader
/// raccoglie la settimana in un ordine "Ct connect" pagato, con le stesse righe di nuovo da preparare:
/// prima si chiudevano a mano con un UPDATE sul numero d'ordine.
///
/// Qui ogni riga dell'ordine raccolto si abbina alla riga "hub_pending" della stessa vendita: stesso
/// prodotto Card Trader, ordine "hub_pending" precedente, e fra più candidati il più recente non ancora
/// abbinato. Una riga già preparata durante la settimana risulta preparata anche nell'ordine raccolto;
/// una non ancora preparata resta da preparare una volta sola, sull'ordine raccolto, e la sua gemella
/// "hub_pending" si chiude. In "Articoli da preparare" resta così solo quello che manca davvero.
///
/// L'abbinamento si ricorda su <see cref="OrderItem.HubOrderItemId"/>: una vendita già abbinata non si
/// riusa per un'altra settimana. Alla prima esecuzione si ripercorre lo storico in ordine di data.
/// Tocca solo il flag "preparato", mai prezzi o quantità.
/// </summary>
public class CtZeroPreparationReconciler
{
    public const string CtConnectBuyer = "Ct connect";
    public const string HubPendingState = "hub_pending";

    private const int SaveBatchSize = 2000;

    private readonly DbContext _db;
    private readonly ILogger _logger;

    public CtZeroPreparationReconciler(DbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    private sealed record HubRow(int Id, int ProductId, int Quantity, bool IsPrepared, int CtOrderId);

    /// <param name="apply">False = calcola soltanto, senza scrivere (per controllare l'effetto).</param>
    public async Task<CtZeroReconcileResult> ReconcileAsync(bool apply = true, CancellationToken cancellationToken = default)
    {
        var items = _db.Set<OrderItem>();

        var hubs = await items.AsNoTracking()
            .Where(i => i.Order.State == HubPendingState)
            .Select(i => new HubRow(i.Id, i.ProductId, i.Quantity, i.IsPrepared, i.Order.CardTraderOrderId))
            .ToListAsync(cancellationToken);
        if (hubs.Count == 0) return new CtZeroReconcileResult(0, 0, 0, 0);
        var firstHub = hubs.Min(h => h.CtOrderId);

        // Copie di ogni riga "hub_pending" già abbinate a un ordine raccolto.
        var used = await items.AsNoTracking()
            .Where(i => i.HubOrderItemId != null)
            .GroupBy(i => i.HubOrderItemId!.Value)
            .Select(g => new { HubId = g.Key, Units = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.HubId, x => x.Units, cancellationToken);
        var remaining = hubs.ToDictionary(h => h.Id, h => h.Quantity - used.GetValueOrDefault(h.Id));
        var hubsByProduct = hubs
            .GroupBy(h => h.ProductId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.CtOrderId).ThenByDescending(h => h.Id).ToList());

        // Righe degli ordini raccolti non ancora abbinate, dalla prima settimana con ordini "hub_pending".
        var pending = await items.AsNoTracking()
            .Where(i => i.HubOrderItemId == null
                        && i.Order.BuyerUsername == CtConnectBuyer
                        && i.Order.PaidAt != null
                        && i.Order.CardTraderOrderId > firstHub)
            .Select(i => new { i.Id, i.ProductId, i.Quantity, i.IsPrepared, CtOrderId = i.Order.CardTraderOrderId, i.Order.PaidAt })
            .ToListAsync(cancellationToken);

        var links = new Dictionary<int, int>();
        var preparedConsolidated = new HashSet<int>();
        var closedHubs = new HashSet<int>();
        var unmatched = 0;

        foreach (var row in pending.OrderBy(r => r.PaidAt).ThenBy(r => r.CtOrderId).ThenBy(r => r.Id))
        {
            // Lo stato "preparato" della riga hub è quello letto all'inizio: chiuderla qui per una copia
            // non deve far risultare preparate le altre copie della stessa riga.
            var hub = hubsByProduct.GetValueOrDefault(row.ProductId)?
                .FirstOrDefault(h => h.CtOrderId < row.CtOrderId && remaining[h.Id] >= row.Quantity);
            if (hub == null)
            {
                unmatched++;
                continue;
            }

            remaining[hub.Id] -= row.Quantity;
            links[row.Id] = hub.Id;
            if (hub.IsPrepared && !row.IsPrepared) preparedConsolidated.Add(row.Id);
            if (!hub.IsPrepared) closedHubs.Add(hub.Id);
        }

        var result = new CtZeroReconcileResult(links.Count, preparedConsolidated.Count, closedHubs.Count, unmatched);
        if (!apply || links.Count == 0) return result;

        // La riga da modificare: quella già in memoria (la sincronizzazione degli ordini tiene traccia
        // delle righe che ha appena scritto), altrimenti una riga vuota con il solo id, così si scrivono
        // solo i campi cambiati.
        var local = _db.ChangeTracker.Entries<OrderItem>().ToDictionary(e => e.Entity.Id, e => e.Entity);
        OrderItem Tracked(int id)
        {
            if (local.TryGetValue(id, out var item)) return item;
            var stub = new OrderItem { Id = id };
            _db.Set<OrderItem>().Attach(stub);
            local[id] = stub;
            return stub;
        }

        var pendingWrites = 0;
        foreach (var (rowId, hubId) in links)
        {
            var row = Tracked(rowId);
            row.HubOrderItemId = hubId;
            if (preparedConsolidated.Contains(rowId)) row.IsPrepared = true;
            if (++pendingWrites % SaveBatchSize == 0) await _db.SaveChangesAsync(cancellationToken);
        }
        foreach (var hubId in closedHubs)
        {
            Tracked(hubId).IsPrepared = true;
            if (++pendingWrites % SaveBatchSize == 0) await _db.SaveChangesAsync(cancellationToken);
        }
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Card Trader Zero: {Linked} righe degli ordini raccolti abbinate, {Prepared} segnate preparate, {Closed} righe hub_pending chiuse, {Unmatched} senza gemella",
            result.Linked, result.ConsolidatedMarkedPrepared, result.HubRowsClosed, result.Unmatched);
        return result;
    }
}

/// <param name="Linked">Righe degli ordini raccolti abbinate in questa esecuzione.</param>
/// <param name="ConsolidatedMarkedPrepared">Di queste, segnate preparate perché la gemella era già preparata.</param>
/// <param name="HubRowsClosed">Righe "hub_pending" non preparate chiuse: si preparano sull'ordine raccolto.</param>
/// <param name="Unmatched">Righe degli ordini raccolti senza gemella: restano com'erano.</param>
public record CtZeroReconcileResult(int Linked, int ConsolidatedMarkedPrepared, int HubRowsClosed, int Unmatched);
