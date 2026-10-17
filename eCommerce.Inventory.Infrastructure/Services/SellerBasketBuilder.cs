using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Carrello di un solo venditore (feature 007, "Per venditore"): l'utente compra per venditore, per pagare una
/// spedizione sola, che su più venditori si mangia il guadagno.
///
/// Regole decise con l'utente il 17/10: serve almeno un prodotto "traino" da aprire con resa alta (50%), che paga
/// la spedizione; poi entrano gli altri suoi prodotti da aprire con resa almeno del 33%, e quelli da tenere a un
/// prezzo non oltre il trend più il 10%, perché la spedizione è già pagata. Il venditore conviene se il guadagno
/// delle aperture, tolta la spedizione, è almeno il 20% di quanto si spende per aprirle. Codice puro.
/// </summary>
public static class SellerBasketBuilder
{
    /// <summary>Oltre questa resa il dato è quasi sempre un errore di prezzo, come negli avvisi.</summary>
    private const decimal MaxRealisticRoi = 300m;

    /// <param name="offers">Offerte di un solo venditore (o del carrello Card Trader Zero).</param>
    /// <param name="basketSeller">Nome del carrello quando le offerte sono di più venditori (Card Trader Zero).</param>
    /// <param name="maxTotal">
    /// Spesa massima del carrello, spedizione compresa (il budget): il carrello si riempie col traino, poi con le aperture
    /// che rendono di più, poi con i tenuti dal punteggio più alto, finché ci stanno. Prima prova del 17/10: senza
    /// questo tetto il carrello Card Trader Zero prendeva tutto, 6.783 € con un budget di 500.
    /// </param>
    /// <returns>Null se il venditore non ha un traino che sta nel budget o il carrello non vale la spedizione.</returns>
    public static SellerBasket? Build(
        string sellerKey, IReadOnlyCollection<SellerOffer> offers, IReadOnlyDictionary<int, SellerProduct> products,
        decimal shipping, SellerRules rules, string? basketSeller = null, decimal maxTotal = decimal.MaxValue)
    {
        // Una copia per prodotto, all'offerta più economica del venditore.
        var cheapest = offers
            .Where(o => o.Price > 0 && products.ContainsKey(o.ProductId))
            .GroupBy(o => o.ProductId)
            .Select(g => g.OrderBy(o => o.Price).First())
            .ToList();

        var open = new List<SellerBasketLine>();
        var hold = new List<SellerBasketLine>();
        foreach (var offer in cheapest)
        {
            var p = products[offer.ProductId];
            var roi = RoiAt(p, offer.Price);
            if (roi is { } r && r >= rules.AddOnRoiPercent && r <= MaxRealisticRoi)
                open.Add(new SellerBasketLine(offer, p, SellerLineKind.Open, Math.Round(r, 1), p.OpenValueNet!.Value - offer.Price));
            else if (p.IsHoldCandidate && p.CmTrend is > 0 && offer.Price <= p.CmTrend.Value * (1 + rules.MaxHoldPremiumPercent / 100))
                hold.Add(new SellerBasketLine(offer, p, SellerLineKind.Hold, roi is { } h ? Math.Round(h, 1) : null, null));
        }

        // Spedizione: quella letta dalla pagina del venditore se c'è (la più alta fra gli articoli scelti: più articoli
        // insieme non costano meno del più pesante), altrimenti quella stimata per il negozio.
        decimal ShippingOf(SellerBasketLine l) => l.Offer.Shipping ?? shipping;

        // Il traino: l'apertura che rende di più fra quelle che stanno nel budget con la spedizione.
        var anchor = open
            .Where(l => l.RoiPercent >= rules.AnchorRoiPercent && l.Offer.Price + ShippingOf(l) <= maxTotal)
            .OrderByDescending(l => l.RoiPercent)
            .FirstOrDefault();
        if (anchor == null) return null;

        var basketShipping = ShippingOf(anchor);
        var items = anchor.Offer.Price;
        var chosenOpen = new List<SellerBasketLine> { anchor };
        foreach (var l in open.Where(l => l != anchor).OrderByDescending(l => l.RoiPercent))
        {
            var withShipping = Math.Max(basketShipping, ShippingOf(l));
            if (items + l.Offer.Price + withShipping > maxTotal) continue;
            chosenOpen.Add(l);
            items += l.Offer.Price;
            basketShipping = withShipping;
        }
        var chosenHold = new List<SellerBasketLine>();
        foreach (var l in hold.OrderByDescending(l => l.Product.HoldScore))
        {
            var withShipping = Math.Max(basketShipping, ShippingOf(l));
            if (items + l.Offer.Price + withShipping > maxTotal) continue;
            chosenHold.Add(l);
            items += l.Offer.Price;
            basketShipping = withShipping;
        }
        open = chosenOpen;
        hold = chosenHold;
        shipping = basketShipping;

        var openCost = open.Sum(l => l.Offer.Price);
        var openGain = open.Sum(l => l.Gain!.Value) - shipping;
        if (openGain < openCost * rules.MinNetPercent / 100) return null;

        var lines = open.OrderByDescending(l => l.RoiPercent).Concat(hold.OrderByDescending(l => l.Product.HoldScore)).ToList();
        var first = offers.First();
        return new SellerBasket(sellerKey, first.Store, basketSeller ?? first.Seller, basketSeller == null ? first.Country : null, shipping,
            Math.Round(openCost, 2), Math.Round(hold.Sum(l => l.Offer.Price), 2), Math.Round(openGain, 2),
            openCost > 0 ? Math.Round(openGain / openCost * 100, 1) : 0,
            Math.Round(hold.Sum(l => l.Product.OpenValueNet ?? 0), 2), lines, false);
    }

    /// <summary>
    /// Venditori in ordine di guadagno netto; si segnano quelli che stanno nel budget, prendendoli in quell'ordine
    /// (un carrello che non ci sta si salta, uno più piccolo dopo può starci).
    /// </summary>
    public static List<SellerBasket> RankAndFit(IEnumerable<SellerBasket> baskets, decimal budget)
    {
        var spent = 0m;
        return baskets
            .OrderByDescending(b => b.OpenGain)
            .Select(b =>
            {
                if (spent + b.Total > budget) return b;
                spent += b.Total;
                return b with { InBudget = true };
            })
            .ToList();
    }

    /// <summary>Resa dell'apertura al prezzo dell'offerta; null se il valore atteso non è affidabile.</summary>
    public static decimal? RoiAt(SellerProduct p, decimal price) =>
        IsOpenable(p.Decision) && p.OpenValueNet is > 0 && price > 0 ? (p.OpenValueNet.Value - price) / price * 100 : null;

    /// <summary>
    /// Il valore atteso dell'apertura è affidabile: "Apri" o "Tieni sigillato". La seconda vuol dire che al trend rende
    /// di più rivenderlo chiuso, ma comprato da un venditore sotto il trend aprirlo può rendere bene (esempio
    /// dell'utente del 17/10: un box del Signore degli Anelli a buon prezzo). Restano fuori dati incompleti, prezzi
    /// dubbi, prodotti non giocabili o senza offerte.
    /// </summary>
    public static bool IsOpenable(string? decision) => decision is "Apri" or "Tieni sigillato";
}

/// <param name="AnchorRoiPercent">Resa minima del prodotto traino (50).</param>
/// <param name="AddOnRoiPercent">Resa minima delle altre aperture (33).</param>
/// <param name="MinNetPercent">Guadagno netto minimo delle aperture, spedizione tolta, sulla loro spesa (20).</param>
/// <param name="MaxHoldPremiumPercent">Quanto sopra il trend si paga un prodotto da tenere (10).</param>
public record SellerRules(decimal AnchorRoiPercent, decimal AddOnRoiPercent, decimal MinNetPercent, decimal MaxHoldPremiumPercent);

/// <param name="OpenValueNet">Valore atteso netto dell'apertura (dalla classifica di oggi).</param>
public record SellerProduct(
    int ProductId, string Name, string SetCode, string? Decision, decimal? OpenValueNet, decimal? CmTrend,
    decimal? HoldScore, bool IsHoldCandidate, int? CardTraderBlueprintId, string? CardmarketName);

/// <param name="Store">"Card Trader", "Card Trader Zero" o "Cardmarket".</param>
/// <param name="CardTraderProductId">Offerta Card Trader, per il carrello via API.</param>
/// <param name="Shipping">Spedizione dell'articolo letta dalla pagina Cardmarket; null = quella stimata del negozio.</param>
public record SellerOffer(
    string Store, string Seller, string? Country, int ProductId, decimal Price, int Available, bool CtZero,
    int? CardTraderProductId, DateTime? PastedAt, decimal? Shipping = null);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SellerLineKind { Open, Hold }

/// <param name="Gain">Valore atteso netto meno prezzo, solo per le aperture.</param>
public record SellerBasketLine(SellerOffer Offer, SellerProduct Product, SellerLineKind Kind, decimal? RoiPercent, decimal? Gain);

/// <param name="OpenGain">Guadagno atteso delle aperture, spedizione tolta.</param>
/// <param name="NetPercent">OpenGain sulla spesa per le aperture.</param>
/// <param name="HoldValue">Valore delle carte dei tenuti (il loro caso peggiore).</param>
/// <param name="InBudget">Sta nel budget, prendendo i venditori in ordine di guadagno.</param>
public record SellerBasket(
    string Key, string Store, string Seller, string? Country, decimal Shipping, decimal OpenCost, decimal HoldCost,
    decimal OpenGain, decimal NetPercent, decimal HoldValue, List<SellerBasketLine> Lines, bool InBudget)
{
    public decimal Total => OpenCost + HoldCost + Shipping;
}
