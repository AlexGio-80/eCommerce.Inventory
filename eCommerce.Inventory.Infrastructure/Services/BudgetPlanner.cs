using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Scelta dentro il budget del pianificatore (feature 007, Fase 0): quali offerte comprare, e quante copie,
/// perché il guadagno sia il più alto possibile senza superare il budget, spedizioni comprese.
///
/// Due parti di budget decise dall'utente: aprire (guadagno atteso in euro) e tenere (punteggio, senza un
/// guadagno in euro confrontabile). Ciò che avanza a una parte passa all'altra. La spedizione si paga una
/// volta per venditore: un secondo prodotto dallo stesso venditore costa solo il suo prezzo.
///
/// È il "problema dello zaino" con le spedizioni; con qualche centinaio di offerte basta scegliere
/// un'offerta alla volta, la migliore per guadagno rispetto al costo. Codice puro, senza database.
/// </summary>
public static class BudgetPlanner
{
    /// <param name="maxHoldProductShare">
    /// Quota massima della parte "tenere" che un solo prodotto può prendere (prezzo di tutte le copie, 0–1):
    /// l'utente preferisce più prodotti diversi a uno solo col punteggio più alto (prova del 17/10: con 500 €
    /// due prodotti si prendevano 434 €). 1 = nessun tetto.
    /// </param>
    public static BudgetChoice Choose(
        IReadOnlyList<BudgetOffer> offers,
        decimal budget,
        decimal openShare,
        IReadOnlyDictionary<string, decimal> shippingByKey,
        IReadOnlyCollection<string>? excludedOfferKeys = null,
        decimal maxHoldProductShare = 1m)
    {
        var excluded = excludedOfferKeys?.ToHashSet() ?? new HashSet<string>();
        var pool = offers.Where(o => !excluded.Contains(o.OfferKey) && o.UnitPrice > 0 && o.Available > 0).ToList();

        var state = new State(shippingByKey);
        var openBudget = Math.Round(budget * Math.Clamp(openShare, 0m, 1m), 2);

        // Aprire con la sua parte, tenere con la sua più l'avanzo, poi di nuovo aprire con quel che resta.
        var openLeft = FillOpen(pool, state, openBudget);
        var holdBudget = budget - openBudget + openLeft;
        var holdLeft = FillHold(pool, state, holdBudget, holdBudget * Math.Clamp(maxHoldProductShare, 0m, 1m));
        FillOpen(pool, state, holdLeft);

        return new BudgetChoice(state.Lines.ToList(), state.ShippingPaid.ToDictionary(k => k.Key, k => k.Value));
    }

    /// <summary>Aggiunge la migliore offerta da aprire finché c'è guadagno e budget; restituisce il budget rimasto.</summary>
    private static decimal FillOpen(List<BudgetOffer> pool, State state, decimal remaining)
    {
        while (true)
        {
            BudgetOffer? best = null;
            decimal bestRatio = 0, bestGain = 0, bestCost = 0;
            foreach (var o in pool.Where(o => o.Kind != BudgetKind.Hold && state.CanTake(o)))
            {
                var shipping = state.ShippingFor(o);
                var cost = o.UnitPrice + shipping;
                if (cost > remaining) continue;
                var gain = o.UnitValue - o.UnitPrice - shipping;
                if (gain <= 0) continue;
                var ratio = gain / cost;
                if (best == null || ratio > bestRatio || (ratio == bestRatio && gain > bestGain))
                {
                    best = o; bestRatio = ratio; bestGain = gain; bestCost = cost;
                }
            }
            if (best == null) return remaining;
            state.Take(best);
            remaining -= bestCost;
        }
    }

    /// <summary>
    /// Tenere: il prodotto col punteggio più alto, alla sua offerta che costa meno (spedizione compresa, se
    /// il venditore è nuovo), finché c'è budget. Un'offerta il cui costo con la spedizione supera
    /// <see cref="BudgetOffer.MaxCost"/> resta fuori: un mazzo da 15 € comprato a 10 € più 12 € di spedizione non
    /// è un buon acquisto da tenere (prima prova del 17/10). Con un'altra riga dallo stesso venditore la
    /// spedizione è già pagata, e l'offerta può rientrare.
    /// </summary>
    private static decimal FillHold(List<BudgetOffer> pool, State state, decimal remaining, decimal maxPerProduct)
    {
        while (true)
        {
            var best = pool
                .Where(o => o.Kind == BudgetKind.Hold && state.CanTake(o))
                .Where(o => state.SpentOn(o.ProductKey) + o.UnitPrice <= maxPerProduct)
                .Select(o => new { Offer = o, Cost = o.UnitPrice + state.ShippingFor(o) })
                .Where(x => x.Cost <= remaining && (x.Offer.MaxCost == null || x.Cost <= x.Offer.MaxCost))
                .OrderByDescending(x => x.Offer.UnitValue)
                .ThenBy(x => x.Cost)
                .FirstOrDefault();
            if (best == null) return remaining;
            state.Take(best.Offer);
            remaining -= best.Cost;
        }
    }

    private sealed class State
    {
        private readonly IReadOnlyDictionary<string, decimal> _shipping;
        private readonly Dictionary<string, int> _copiesByProduct = new();
        private readonly Dictionary<string, BudgetLine> _lines = new();

        public State(IReadOnlyDictionary<string, decimal> shipping) => _shipping = shipping;

        public Dictionary<string, decimal> ShippingPaid { get; } = new();

        public IEnumerable<BudgetLine> Lines => _lines.Values;

        /// <summary>Prezzo delle copie già prese di un prodotto, spedizioni escluse.</summary>
        public decimal SpentOn(string productKey) =>
            _lines.Values.Where(l => l.Offer.ProductKey == productKey).Sum(l => l.Offer.UnitPrice * l.Quantity);

        public decimal ShippingFor(BudgetOffer o) =>
            ShippingPaid.ContainsKey(o.ShippingKey) ? 0 : _shipping.GetValueOrDefault(o.ShippingKey);

        public bool CanTake(BudgetOffer o) =>
            _copiesByProduct.GetValueOrDefault(o.ProductKey) < o.MaxCopies
            && (_lines.TryGetValue(o.OfferKey, out var line) ? line.Quantity : 0) < o.Available;

        public void Take(BudgetOffer o)
        {
            if (!ShippingPaid.ContainsKey(o.ShippingKey))
                ShippingPaid[o.ShippingKey] = _shipping.GetValueOrDefault(o.ShippingKey);
            _copiesByProduct[o.ProductKey] = _copiesByProduct.GetValueOrDefault(o.ProductKey) + 1;
            _lines[o.OfferKey] = _lines.TryGetValue(o.OfferKey, out var line)
                ? line with { Quantity = line.Quantity + 1 }
                : new BudgetLine(o, 1);
        }
    }
}

/// <summary>Nel JSON come testo ("Open", "Hold", "SecretLair"): la scheda li mostra e li confronta.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BudgetKind { Open, Hold, SecretLair }

/// <summary>Un'offerta che il pianificatore può comprare.</summary>
/// <param name="OfferKey">Unica per offerta (es. "ct:123456", "cm:product:42", "wz:ABC").</param>
/// <param name="ProductKey">Il prodotto: le copie si contano su questo, fra tutte le offerte.</param>
/// <param name="ShippingKey">Chi spedisce: la spedizione si paga una volta per chiave.</param>
/// <param name="UnitValue">Aprire: valore atteso netto di una copia. Tenere: punteggio 0–100.</param>
/// <param name="MaxCopies">Copie al massimo di questo prodotto (uguale per tutte le sue offerte).</param>
/// <param name="MaxCost">Solo tenere: costo massimo di una copia, spedizione da pagare compresa; null = nessun tetto.</param>
public record BudgetOffer(
    string OfferKey,
    string ProductKey,
    string ShippingKey,
    BudgetKind Kind,
    decimal UnitPrice,
    decimal UnitValue,
    int Available,
    int MaxCopies,
    decimal? MaxCost = null);

public record BudgetLine(BudgetOffer Offer, int Quantity);

/// <param name="ShippingPaid">Spedizioni messe in conto, per chiave.</param>
public record BudgetChoice(List<BudgetLine> Lines, Dictionary<string, decimal> ShippingPaid)
{
    public decimal Spent => Lines.Sum(l => l.Offer.UnitPrice * l.Quantity) + ShippingPaid.Values.Sum();
}
