using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Lotti di carte venduti fuori da Card Trader (es. 100 bulk su Subito). Si registrano come un ordine pagato, con una
/// riga per Tag: così contano come vendite nei report, nella colonna "Venduto" del registro acquisti, nella
/// redditività per Tag e nella quota venduta del bulk dell'uscita, come se fossero vendute su Card Trader.
///
/// L'ordine ha un <see cref="Order.CardTraderOrderId"/> negativo: la sincronizzazione lavora per ID Card Trader e non
/// lo tocca, e la commissione Card Trader si misura solo sugli ordini veri. Nasce già preparato e completato.
/// L'espansione della riga si ricava dal Tag (CODICE_..., es. "#FRA_OLD" → Reality Fracture), senza blueprint:
/// le carte del lotto non sono note una per una.
/// </summary>
public class ExternalLotService
{
    public const string State = "external";

    private readonly ApplicationDbContext _db;

    public ExternalLotService(ApplicationDbContext db) => _db = db;

    public async Task<Order> RegisterAsync(ExternalLotInput input, CancellationToken cancellationToken = default)
    {
        var site = input.Site?.Trim();
        if (string.IsNullOrEmpty(site)) throw new ArgumentException("Indica il sito di vendita.");
        if (input.Total <= 0) throw new ArgumentException("L'incasso deve essere maggiore di zero.");
        var lines = (input.Lines ?? new List<ExternalLotLine>())
            .Where(l => !string.IsNullOrWhiteSpace(l.Tag) && l.Quantity > 0)
            .Select(l => (Tag: "#" + OpeningBalanceService.NormalizeTag(l.Tag), l.Quantity))
            .ToList();
        if (lines.Count == 0) throw new ArgumentException("Indica almeno un Tag con il numero di carte.");

        var codes = lines.Select(l => ExpansionCodeOf(l.Tag)).Distinct().ToList();
        var expansions = (await _db.Expansions.AsNoTracking()
                .Where(e => codes.Contains(e.Code.ToUpper()))
                .Select(e => new { e.Code, e.Name })
                .ToListAsync(cancellationToken))
            .GroupBy(e => e.Code.ToUpperInvariant())
            .ToDictionary(g => g.Key, g => g.First().Name);

        var minId = await _db.Orders.Select(o => (int?)o.CardTraderOrderId).MinAsync(cancellationToken) ?? 0;
        var id = Math.Min(minId, 0) - 1;
        var paidAt = input.Date.ToDateTime(new TimeOnly(12, 0));
        var total = Math.Round(input.Total, 2);
        var cards = lines.Sum(l => l.Quantity);

        var order = new Order
        {
            CardTraderOrderId = id,
            Code = $"LOTTO{-id}",
            TransactionCode = string.Empty,
            BuyerUsername = Truncate(site, 100),
            State = State,
            PaidAt = paidAt,
            SentAt = paidAt,
            SellerTotal = total,
            SellerFee = 0,
            SellerSubtotal = total,
            IsCompleted = true
        };
        foreach (var (tag, quantity, price) in Allocate(lines, total))
        {
            var expansion = expansions.GetValueOrDefault(ExpansionCodeOf(tag)) ?? string.Empty;
            order.OrderItems.Add(new OrderItem
            {
                Name = Truncate(string.IsNullOrWhiteSpace(input.Notes) ? $"Lotto {site}: {cards} carte" : input.Notes.Trim(), 200),
                ExpansionName = expansion,
                Quantity = quantity,
                Price = price,
                Condition = string.Empty,
                Language = string.Empty,
                Tag = tag,
                IsPrepared = true
            });
        }
        _db.Orders.Add(order);
        await _db.SaveChangesAsync(cancellationToken);
        return order;
    }

    /// <summary>Toglie un lotto registrato per errore. Solo i lotti: un ordine Card Trader non si cancella da qui.</summary>
    public async Task<bool> DeleteAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var order = await _db.Orders.Include(o => o.OrderItems)
            .FirstOrDefaultAsync(o => o.Id == orderId && o.CardTraderOrderId < 0 && o.State == State, cancellationToken);
        if (order == null) return false;
        _db.OrderItems.RemoveRange(order.OrderItems);
        _db.Orders.Remove(order);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>"#FRA_OLD" → "FRA"; "#TRK_PB_20261115" → "TRK".</summary>
    public static string ExpansionCodeOf(string tag) => OpeningBalanceService.NormalizeTag(tag).Split('_')[0];

    /// <summary>
    /// L'incasso diviso in parti uguali sulle carte, al centesimo: i centesimi che avanzano vanno alle prime carte, su
    /// una riga a parte, così la somma delle righe è esattamente l'incasso (es. 7 € su 30 carte: 10 a 0,24 e 20 a 0,23).
    /// </summary>
    public static List<(string Tag, int Quantity, decimal Price)> Allocate(List<(string Tag, int Quantity)> lines, decimal total)
    {
        var cents = (long)Math.Round(total * 100);
        var cards = lines.Sum(l => l.Quantity);
        var unit = cents / cards;
        var extra = cents % cards;
        var result = new List<(string, int, decimal)>();
        foreach (var (tag, quantity) in lines)
        {
            var up = (int)Math.Min(extra, quantity);
            extra -= up;
            if (up > 0) result.Add((tag, up, (unit + 1) / 100m));
            if (quantity - up > 0) result.Add((tag, quantity - up, unit / 100m));
        }
        return result;
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <param name="Site">Dove si è venduto (Subito, Vinted, un negozio...).</param>
/// <param name="Total">Incasso del lotto, già netto: fuori da Card Trader non c'è commissione.</param>
/// <param name="Notes">Descrizione facoltativa, usata come nome delle righe.</param>
public record ExternalLotInput(string? Site, DateOnly Date, decimal Total, List<ExternalLotLine>? Lines, string? Notes = null);

/// <param name="Tag">Tag delle carte vendute, come sulle inserzioni (es. "#FRA_OLD").</param>
/// <param name="Quantity">Carte del lotto con quel Tag.</param>
public record ExternalLotLine(string Tag, int Quantity);
