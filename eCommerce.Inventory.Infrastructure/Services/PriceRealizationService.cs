using System.Text.Json;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Quanto si incassa davvero vendendo una carta, rispetto al trend Cardmarket: il valore atteso
/// usa il trend CM, ma si vende su Card Trader e a prezzi propri.
///
/// Il rapporto si misura per tipo di carta (<see cref="CardKind"/>), perché cambia molto: al
/// 16/10/2026 le carte delle buste (quasi tutte di un'uscita recente) si vendevano a 1,28 volte il
/// trend, quelle dei mazzi Commander a 0,95, i Secret Lair a 1,16. Un fattore unico (allora 1,17)
/// gonfiava il valore dei mazzi. Un tipo con un campione troppo piccolo usa il fattore complessivo.
///
/// Si confrontano solo le vendite recenti (<see cref="WindowDays"/>) con l'ultimo trend: il trend
/// delle singole cala nelle settimane dopo l'uscita, e confrontare vendite vecchie con il trend di
/// oggi gonfierebbe il rapporto. Sotto <see cref="MinTrend"/> l'abbinamento automatico fra Card Trader
/// e Cardmarket è troppo rumoroso (varianti abbinate al prodotto sbagliato), e quelle carte sono
/// comunque governate dalle regole del bulk.
///
/// Le vendite Card Trader Zero arrivano prima come ordini "hub_pending", senza pagamento, e una o due
/// settimane dopo come righe dell'ordine "Ct connect" pagato, abbinate alla loro gemella
/// (<see cref="Domain.Entities.OrderItem.HubOrderItemId"/>). Si contano le righe pagate e le
/// "hub_pending" non ancora abbinate: senza queste ultime le vendite degli ultimi giorni mancherebbero,
/// e sono proprio quelle delle aperture più recenti. Gli ordini "hub_pending" non hanno data: per
/// restare nella finestra si tengono quelli con un numero d'ordine Card Trader non più vecchio del
/// primo ordine pagato nella finestra (i numeri crescono nel tempo). Le righe a prezzo 0 (omaggi,
/// righe tecniche) sono escluse.
/// </summary>
public class PriceRealizationService
{
    public const int WindowDays = 30;
    public const decimal MinTrend = 1m;

    /// <summary>Sotto questo valore di trend venduto il campione è troppo piccolo.</summary>
    private const decimal MinSampleTrend = 100m;

    private const string HubPendingState = CtZeroPreparationReconciler.HubPendingState;

    private readonly ApplicationDbContext _db;

    public PriceRealizationService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PriceRealization> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddDays(-WindowDays);

        var firstPaidOrder = await _db.Orders.AsNoTracking()
            .Where(o => o.PaidAt >= since && o.State != HubPendingState)
            .MinAsync(o => (int?)o.CardTraderOrderId, cancellationToken);

        var sales = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Price > 0 && oi.Blueprint != null && oi.Blueprint.CardMarketIds != null)
            .Where(oi => oi.Order.PaidAt >= since
                         || (firstPaidOrder != null
                             && oi.Order.State == HubPendingState
                             && oi.Order.CardTraderOrderId >= firstPaidOrder
                             && !_db.OrderItems.Any(paid => paid.HubOrderItemId == oi.Id)))
            .Select(oi => new { oi.Quantity, oi.Price, oi.IsFoil, oi.Blueprint!.CardMarketIds, oi.Blueprint.ScryfallId })
            .ToListAsync(cancellationToken);

        var rows = sales
            .Select(s => new { s.Quantity, s.Price, s.IsFoil, s.ScryfallId, CmId = FirstCardmarketId(s.CardMarketIds) })
            .Where(s => s.CmId.HasValue)
            .ToList();

        var ids = rows.Select(r => r.CmId!.Value).Distinct().ToList();
        var trends = await _db.CardmarketLatestPrices.AsNoTracking()
            .Where(p => ids.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, cancellationToken);

        // Tipo di carta dal set MTGJSON della stampa, la stessa regola usata dal valore atteso.
        var scryfallIds = rows.Where(r => r.ScryfallId != null).Select(r => r.ScryfallId!).Distinct().ToList();
        var setTypes = await _db.MtgjsonSets.AsNoTracking().ToDictionaryAsync(s => s.Code, s => s.Type, cancellationToken);
        var kindByScryfall = (await _db.MtgjsonCards.AsNoTracking()
                .Where(c => c.ScryfallId != null && scryfallIds.Contains(c.ScryfallId))
                .Select(c => new { c.ScryfallId, c.SetCode })
                .ToListAsync(cancellationToken))
            .GroupBy(c => c.ScryfallId!)
            .ToDictionary(g => g.Key, g => CardKinds.Of(g.First().SetCode, setTypes.GetValueOrDefault(g.First().SetCode)));

        var total = new Sample();
        var byKind = Enum.GetValues<CardKind>().ToDictionary(k => k, _ => new Sample());
        foreach (var row in rows)
        {
            if (!trends.TryGetValue(row.CmId!.Value, out var price)) continue;
            var trend = row.IsFoil ? price.TrendFoil : price.Trend;
            if (trend is not { } t || t < MinTrend) continue;

            total.Add(row.Quantity, row.Price, t);
            // Una carta senza stampa MTGJSON abbinata conta solo nel complessivo.
            if (row.ScryfallId != null && kindByScryfall.TryGetValue(row.ScryfallId, out var kind))
                byKind[kind].Add(row.Quantity, row.Price, t);
        }

        var overall = total.Factor ?? 1m;
        return new PriceRealization(
            overall, total.Factor.HasValue, total.Copies, Math.Round(total.Revenue, 2), Math.Round(total.TrendValue, 2),
            byKind.Select(kv => new KindRealization(
                    kv.Key, kv.Value.Factor ?? overall, kv.Value.Factor.HasValue,
                    kv.Value.Copies, Math.Round(kv.Value.Revenue, 2), Math.Round(kv.Value.TrendValue, 2)))
                .ToList());
    }

    private sealed class Sample
    {
        public int Copies { get; private set; }
        public decimal Revenue { get; private set; }
        public decimal TrendValue { get; private set; }

        public void Add(int quantity, decimal price, decimal trend)
        {
            Copies += quantity;
            Revenue += quantity * price;
            TrendValue += quantity * trend;
        }

        /// <summary>Null se il campione è troppo piccolo.</summary>
        public decimal? Factor => TrendValue >= MinSampleTrend ? Math.Round(Revenue / TrendValue, 3) : null;
    }

    /// <summary><see cref="Domain.Entities.Blueprint.CardMarketIds"/> è un array JSON: si prende il primo id.</summary>
    public static int? FirstCardmarketId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                   && doc.RootElement[0].TryGetInt32(out var id)
                ? id
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Tipo di carta per il prezzo realizzato: si vende in modo diverso, quindi si misura a parte.</summary>
public enum CardKind
{
    /// <summary>Carte delle espansioni, che si trovano nelle buste.</summary>
    Booster,

    /// <summary>Carte dei set di mazzi: Commander, Duel Deck, mazzi introduttivi e simili.</summary>
    Deck,

    SecretLair
}

public static class CardKinds
{
    private static readonly IReadOnlySet<string> DeckSetTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "commander", "duel_deck", "premium_deck", "planechase", "archenemy", "starter"
    };

    private static readonly IReadOnlySet<string> SecretLairSets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "SLD", "SLC", "SLU", "SLP"
    };

    /// <summary>Tipo di una carta dal set MTGJSON della sua stampa; quello che non è mazzo né Secret Lair è busta.</summary>
    public static CardKind Of(string setCode, string? setType) =>
        SecretLairSets.Contains(setCode) ? CardKind.SecretLair
        : setType != null && DeckSetTypes.Contains(setType) ? CardKind.Deck
        : CardKind.Booster;
}

/// <param name="Factor">Incassato / trend CM su tutte le vendite (1,17 = si incassa il 17% in più del trend).</param>
/// <param name="Measured">False se il campione era troppo piccolo e si usa 1.</param>
/// <param name="Kinds">Lo stesso rapporto per tipo di carta.</param>
public record PriceRealization(decimal Factor, bool Measured, int Copies, decimal Revenue, decimal TrendValue, List<KindRealization> Kinds)
{
    public decimal FactorFor(CardKind kind) => Kinds.FirstOrDefault(k => k.Kind == kind)?.Factor ?? Factor;
}

/// <param name="Factor">Rapporto del tipo; il complessivo se il campione del tipo è troppo piccolo.</param>
/// <param name="Measured">False se il campione del tipo era troppo piccolo.</param>
public record KindRealization(CardKind Kind, decimal Factor, bool Measured, int Copies, decimal Revenue, decimal TrendValue);
