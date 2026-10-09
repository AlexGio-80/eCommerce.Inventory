using System.Globalization;
using System.Text.RegularExpressions;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>Valutazione dei prodotti del negozio Secret Lair, per il monitoraggio e gli avvisi.</summary>
public interface ISecretLairDropValuation
{
    /// <summary>Congela la stima dei prodotti che hanno le carte ma non ancora prezzi propri; restituisce quanti.</summary>
    Task<int> FreezeEstimatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Valutazione dei prodotti indicati (id del database), per chi deve descriverli.</summary>
    Task<Dictionary<int, SecretLairDropEstimate>> EvaluateProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default);
}

/// <summary>
/// Secret Lair, Fase 3: quanto vale un drop prima di comprarlo.
///
/// Per ogni carta del prodotto (dall'elenco del negozio Wizards) si cerca la stampa più economica già
/// esistente su Cardmarket e la si porta al prezzo che la versione Secret Lair ha di solito, con la
/// curva del sovrapprezzo misurata sui drop passati (<see cref="SecretLairPremiumCurve"/>, una per
/// foil e una per non foil). Da lì lo stesso calcolo del valore atteso dei sigillati: prezzo
/// realizzato, quota venduta per fascia, costo per carta, costi di vendita. Il totale si confronta
/// con il prezzo Wizards.
///
/// Le carte mai stampate prima valgono il prezzo mediano delle carte Secret Lair senza altre stampe,
/// e sono una stima debole. Quando il drop esce e le sue carte hanno un prezzo Cardmarket, quei
/// prezzi sostituiscono la stima; quella fatta prima dell'uscita resta congelata nel prodotto
/// (<see cref="SecretLairShopProduct.EstimatedNetValue"/>) per misurare quanto ci aveva preso.
/// </summary>
public class SecretLairDropValuationService : ISecretLairDropValuation
{
    private static readonly string[] SecretLairSets = { "SLD", "SLC", "SLU" };

    /// <summary>Tipi di set MTGJSON che non sono una stampa base credibile (bordo oro, digitali, Un-set).</summary>
    private static readonly string[] ExcludedBaseSetTypes = { "memorabilia", "alchemy", "funny", "vanguard", "token", "minigame" };

    /// <summary>Sotto questo trend un prezzo Cardmarket è di riempimento, non di mercato.</summary>
    private const decimal MinMarketPrice = 0.02m;

    /// <summary>Quota minima di carte del drop con un prezzo per usare i prezzi reali al posto della stima.</summary>
    private const decimal MinRealCoverage = 0.9m;

    private readonly ApplicationDbContext _db;
    private readonly SecretLairRetrospectiveService _retrospective;
    private readonly SealedProductAnalysisService _analysis;
    private readonly IConfiguration _configuration;

    public SecretLairDropValuationService(
        ApplicationDbContext db,
        SecretLairRetrospectiveService retrospective,
        SealedProductAnalysisService analysis,
        IConfiguration configuration)
    {
        _db = db;
        _retrospective = retrospective;
        _analysis = analysis;
        _configuration = configuration;
    }

    private decimal BuyMarginPercent => _configuration.GetValue("Purchasing:SecretLair:BuyMarginPercent", 30m);

    /// <summary>Modello e valutazione di tutti i prodotti visti nel negozio.</summary>
    public async Task<SecretLairValuation> GetAsync(CancellationToken cancellationToken = default)
    {
        var model = await BuildModelAsync(cancellationToken);
        var products = await LoadProductsAsync(null, cancellationToken);
        var evaluator = new Evaluator(model, products, BuyMarginPercent);

        return new SecretLairValuation(
            new SecretLairValuationModel(
                model.NormalCurve.Points.ToList(), model.FoilCurve.Points.ToList(),
                model.NormalCurve.NoBasePrice, model.FoilCurve.NoBasePrice,
                model.NormalCurve.Samples, model.FoilCurve.Samples,
                model.NormalCurve.NoBaseSamples + model.FoilCurve.NoBaseSamples,
                model.Backtest, BuyMarginPercent, model.Settings.Dto),
            products.Select(evaluator.Evaluate).ToList());
    }

    public async Task<Dictionary<int, SecretLairDropEstimate>> EvaluateProductsAsync(
        IReadOnlyCollection<int> productIds, CancellationToken cancellationToken = default)
    {
        var model = await BuildModelAsync(cancellationToken);
        // Tutti i prodotti, non solo quelli richiesti: un bundle si valuta con i drop che contiene.
        var products = await LoadProductsAsync(null, cancellationToken);
        var evaluator = new Evaluator(model, products, BuyMarginPercent);
        return products.Where(p => productIds.Contains(p.Id)).ToDictionary(p => p.Id, evaluator.Evaluate);
    }

    public async Task<int> FreezeEstimatesAsync(CancellationToken cancellationToken = default)
    {
        var model = await BuildModelAsync(cancellationToken);
        var products = await LoadProductsAsync(null, cancellationToken);
        var evaluator = new Evaluator(model, products, BuyMarginPercent);

        var toFreeze = products
            .Where(p => p.EstimatedAt == null && p.ContentsFetchedAt != null)
            .Select(p => (Product: p, Estimate: evaluator.Evaluate(p)))
            .Where(x => x.Estimate.RealNetValue == null && x.Estimate.EstimatedNetValue is > 0)
            .ToList();
        if (toFreeze.Count == 0) return 0;

        var ids = toFreeze.Select(x => x.Product.Id).ToList();
        var tracked = await _db.SecretLairShopProducts.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var (product, estimate) in toFreeze)
        {
            if (!tracked.TryGetValue(product.Id, out var entity)) continue;
            entity.EstimatedNetValue = estimate.EstimatedNetValue;
            entity.EstimatedAt = now;
        }
        await _db.SaveChangesAsync(cancellationToken);
        return toFreeze.Count;
    }

    /// <summary>Riga per l'avviso di un drop nuovo: valore stimato, confronto col prezzo e suggerimento.</summary>
    public static string Describe(SecretLairDropEstimate estimate)
    {
        var euro = CultureInfo.GetCultureInfo("it-IT");
        var net = estimate.RealNetValue ?? estimate.EstimatedNetValue;
        if (net is null || estimate.MarginPercent is null) return $"Stima: {estimate.Verdict}";
        var text = $"Stima: valore netto {net.Value.ToString("C", euro)} contro {estimate.Price.ToString("C", euro)} " +
                   $"({estimate.MarginPercent.Value:+0;-0}%) → {estimate.Verdict}";
        if (estimate.WeakSharePercent >= 50) text += ", stima debole (carte senza stampe precedenti)";
        if (estimate.UnknownLines > 0) text += $", {estimate.UnknownLines} righe non stimate";
        return text;
    }

    private async Task<List<SecretLairShopProduct>> LoadProductsAsync(IReadOnlyCollection<int>? ids, CancellationToken cancellationToken)
    {
        var query = _db.SecretLairShopProducts.AsNoTracking().Include(p => p.Cards).AsQueryable();
        if (ids != null) query = query.Where(p => ids.Contains(p.Id));
        return await query.ToListAsync(cancellationToken);
    }

    // --- Modello ---

    internal sealed record BasePrinting(decimal Price, string SetName);

    internal sealed class Model
    {
        public required SecretLairPremiumCurve NormalCurve { get; init; }
        public required SecretLairPremiumCurve FoilCurve { get; init; }
        public required Dictionary<string, BasePrinting> BaseByName { get; init; }
        public required Dictionary<string, List<RealCard>> RealCardsByDropName { get; init; }
        public required (OpeningValueSettings Values, OpeningValueSettingsDto Dto) Settings { get; init; }
        public required SecretLairBacktest Backtest { get; init; }

        public SecretLairPremiumCurve Curve(bool foil) => foil ? FoilCurve : NormalCurve;
    }

    /// <param name="Price">Prezzo Cardmarket della versione Secret Lair; null se non c'è.</param>
    internal sealed record RealCard(string Name, bool Foil, int Count, decimal? Price);

    private async Task<Model> BuildModelAsync(CancellationToken cancellationToken)
    {
        var settings = await _analysis.ResolveSettingsAsync(null, cancellationToken);

        var setTypes = await _db.MtgjsonSets.AsNoTracking()
            .ToDictionaryAsync(s => s.Code, s => (s.Type, s.Name), cancellationToken);

        var printings = await (
                from m in _db.MtgjsonCards.AsNoTracking()
                where m.CardmarketId != null
                join p in _db.CardmarketLatestPrices.AsNoTracking() on m.CardmarketId equals (int?)p.IdProduct
                select new { m.Name, m.SetCode, CardmarketId = m.CardmarketId!.Value, p.Trend, p.TrendFoil })
            .ToListAsync(cancellationToken);

        // Stampa più economica per nome (anche per la sola faccia frontale delle carte a due facce).
        var baseByName = new Dictionary<string, BasePrinting>();
        foreach (var p in printings)
        {
            if (SecretLairSets.Contains(p.SetCode) || p.Trend is not >= MinMarketPrice) continue;
            var set = setTypes.GetValueOrDefault(p.SetCode);
            if (set.Type != null && ExcludedBaseSetTypes.Contains(set.Type)) continue;
            foreach (var key in NameKeys(p.Name))
            {
                if (!baseByName.TryGetValue(key, out var current) || p.Trend.Value < current.Price)
                    baseByName[key] = new BasePrinting(p.Trend.Value, set.Name ?? p.SetCode);
            }
        }

        var slPrices = printings.Where(p => SecretLairSets.Contains(p.SetCode))
            .GroupBy(p => p.CardmarketId)
            .ToDictionary(g => g.Key, g => (g.First().Trend, g.First().TrendFoil));
        decimal? SlPrice(int? cardmarketId, bool foil)
        {
            if (cardmarketId is not { } id || !slPrices.TryGetValue(id, out var p)) return null;
            var price = foil ? p.TrendFoil : p.Trend;
            return price is >= MinMarketPrice ? price : null;
        }

        var drops = await _db.SealedProducts.AsNoTracking()
            .Include(p => p.Contents)
            .Where(p => SecretLairSets.Contains(p.SetCode))
            .ToListAsync(cancellationToken);
        var dropCards = await _retrospective.LoadDropCardsAsync(
            drops.Where(d => SecretLairRetrospectiveService.TypeOf(d) != SecretLairDropType.Bundle).ToList(), cancellationToken);

        // Campioni della curva: una volta per carta Secret Lair (stessa carta in più drop = un campione).
        var samples = dropCards
            .GroupBy(c => (c.CardmarketId, c.Foil))
            .Select(g => g.First())
            .Select(c => (Card: c, Price: SlPrice(c.CardmarketId, c.Foil), Base: LookupBase(baseByName, c.Name)))
            .Where(x => x.Price.HasValue)
            .ToList();

        SecretLairPremiumCurve Fit(bool foil) => SecretLairPremiumCurve.Fit(
            samples.Where(x => x.Card.Foil == foil && x.Base != null).Select(x => (x.Base!.Price, x.Price!.Value)),
            samples.Where(x => x.Card.Foil == foil && x.Base == null).Select(x => x.Price!.Value));
        var normal = Fit(false);
        var foilCurve = Fit(true);

        // Drop descritti da un mazzo che il catalogo dei mazzi non ha, o ha senza carte (MTGJSON lo
        // pubblica vuoto prima di conoscerne il contenuto): ne resterebbero solo le carte bonus, e il
        // "prezzo reale" sarebbe quello di una carta sola.
        var deckKeys = (await _db.MtgjsonDecks.AsNoTracking()
                .Where(d => SecretLairSets.Contains(d.SetCode) && d.Cards.Any())
                .Select(d => new { d.SetCode, d.Name })
                .ToListAsync(cancellationToken))
            .Select(d => (d.SetCode, d.Name))
            .ToHashSet();
        var incomplete = drops
            .Where(d => d.Contents.Any(c => c.Kind == SealedContentKind.Deck && c.Name != null
                                            && !deckKeys.Contains((c.SetCode ?? "", c.Name))))
            .Select(d => d.Id)
            .ToHashSet();

        var dropsById = drops.ToDictionary(d => d.Id);
        var realByDrop = dropCards
            .Where(c => !incomplete.Contains(c.DropId))
            .GroupBy(c => c.DropId)
            .ToDictionary(g => g.Key, g => g.Select(c => new RealCard(c.Name, c.Foil, c.Count, SlPrice(c.CardmarketId, c.Foil))).ToList());

        var realByName = new Dictionary<string, List<RealCard>>();
        foreach (var (dropId, cards) in realByDrop.OrderBy(kv => kv.Key))
            realByName.TryAdd(SecretLairRetrospectiveService.NormalizeDropName(dropsById[dropId].Name), cards);

        return new Model
        {
            NormalCurve = normal,
            FoilCurve = foilCurve,
            BaseByName = baseByName,
            RealCardsByDropName = realByName,
            Settings = settings,
            Backtest = Backtest(realByDrop, dropsById, baseByName, normal, foilCurve)
        };
    }

    /// <summary>
    /// Quanto ci prende il modello sui drop passati: per ogni drop con tutte le carte prezzate, la
    /// somma dei prezzi stimati dalle stampe base contro la somma dei prezzi reali, ai trend di oggi.
    /// È una verifica sugli stessi drop da cui si misura la curva, quindi un po' ottimista; la prova
    /// vera è la stima congelata prima dell'uscita.
    /// </summary>
    private static SecretLairBacktest Backtest(
        Dictionary<int, List<RealCard>> realByDrop, Dictionary<int, SealedProduct> drops,
        Dictionary<string, BasePrinting> baseByName, SecretLairPremiumCurve normal, SecretLairPremiumCurve foil)
    {
        var errors = new List<decimal>();
        foreach (var (dropId, cards) in realByDrop)
        {
            if (cards.Count == 0 || cards.Any(c => c.Price == null)) continue;
            if (SecretLairRetrospectiveService.TypeOf(drops[dropId]) == SecretLairDropType.Commander) continue;

            decimal predicted = 0, actual = 0;
            foreach (var card in cards)
            {
                var curve = card.Foil ? foil : normal;
                var basePrinting = LookupBase(baseByName, card.Name);
                predicted += card.Count * (basePrinting != null ? curve.Estimate(basePrinting.Price) ?? 0 : curve.NoBasePrice);
                actual += card.Count * card.Price!.Value;
            }
            if (actual > 0) errors.Add((predicted - actual) / actual * 100m);
        }

        if (errors.Count == 0) return new SecretLairBacktest(0, null, null, null);
        var absolute = errors.Select(Math.Abs).OrderBy(e => e).ToList();
        var signed = errors.OrderBy(e => e).ToList();
        return new SecretLairBacktest(
            errors.Count,
            Math.Round(absolute[absolute.Count / 2], 1),
            Math.Round(signed[signed.Count / 2], 1),
            Math.Round((decimal)absolute.Count(e => e <= 25m) / absolute.Count * 100m, 1));
    }

    // --- Nomi ---

    /// <summary>Chiavi di ricerca di un nome di carta: intero e, per le carte a due facce, la faccia frontale.</summary>
    public static IEnumerable<string> NameKeys(string name)
    {
        var key = NormalizeCardName(name);
        yield return key;
        var separator = key.IndexOf(" // ", StringComparison.Ordinal);
        if (separator > 0) yield return key[..separator];
    }

    public static string NormalizeCardName(string name)
    {
        var n = name.Trim().ToLowerInvariant().Replace('’', '\'').Replace('‘', '\'').Replace('´', '\'');
        return Regex.Replace(n, @"\s+", " ");
    }

    private static BasePrinting? LookupBase(Dictionary<string, BasePrinting> baseByName, string name)
    {
        var key = NormalizeCardName(name);
        if (baseByName.TryGetValue(key, out var found)) return found;
        // "Swamps", "Islands": le terre base del negozio sono al plurale.
        return key.EndsWith('s') && baseByName.TryGetValue(key[..^1], out found) ? found : null;
    }

    /// <summary>
    /// Nome della carta da una riga del negozio, togliendo il trattamento davanti ("Foil Sol Ring",
    /// "Pool Party Foil Sol Ring"): in quel caso la carta è foil anche in un prodotto non foil.
    /// </summary>
    public static (string Name, bool Foil) CleanCardLine(string line, bool productFoil)
    {
        var match = Regex.Match(line.Trim(), @"^(?:[\p{L}'-]+\s+){0,2}?Foil\s+(.+)$", RegexOptions.IgnoreCase);
        return match.Success ? (match.Groups[1].Value.Trim(), true) : (line.Trim(), productFoil);
    }

    /// <summary>
    /// Righe che non sono una carta: altri prodotti, carte a sorpresa ("1 rare or mythic rare card"),
    /// gadget, ristampe non elencate ("Non-foil reprints"). Non si stimano.
    /// </summary>
    public static bool LooksLikeNonCard(string line) =>
        Regex.IsMatch(line,
            @"\b(edition|secret lair|cards?|reprints|tokens?|playmat|deck ?box|sleeves|bundle|randomized|promo|art print|booster)\b|:|\d",
            RegexOptions.IgnoreCase);

    // --- Valutazione ---

    private sealed class Evaluator
    {
        private readonly Model _model;
        private readonly decimal _buyMargin;
        private readonly Dictionary<string, SecretLairShopProduct> _productsByName;
        private readonly Dictionary<Guid, decimal> _prices = new();
        private readonly OpeningValueCalculator _calculator;
        private readonly Dictionary<int, Totals> _cache = new();

        public Evaluator(Model model, List<SecretLairShopProduct> products, decimal buyMargin)
        {
            _model = model;
            _buyMargin = buyMargin;
            _productsByName = products
                .GroupBy(p => SecretLairRetrospectiveService.NormalizeDropName(p.Title))
                .ToDictionary(g => g.Key, g => g.OrderBy(p => p.RemovedAt != null).ThenByDescending(p => p.FirstSeenAt).First());
            _calculator = new OpeningValueCalculator(
                (uuid, _) => _prices.TryGetValue(uuid, out var price) ? price : null,
                model.Settings.Values,
                new Dictionary<string, List<BoosterConfig>>(),
                new Dictionary<(string, string), BoosterSheet>(),
                new Dictionary<string, MtgjsonDeck>());
        }

        /// <summary>Valore realizzabile lordo di una copia a questo prezzo (bulk, quota venduta, costo per carta).</summary>
        private decimal ValueOf(decimal price, bool foil)
        {
            var uuid = Guid.NewGuid();
            _prices[uuid] = price;
            return _calculator.CardValue(uuid, foil) ?? 0m;
        }

        public SecretLairDropEstimate Evaluate(SecretLairShopProduct product)
        {
            var totals = Compute(product, 0);
            var settings = _model.Settings.Values;
            decimal? estimatedNet = totals.EstimatedValue > 0 ? Math.Round(settings.Net(totals.EstimatedValue), 2) : null;
            decimal? realNet = totals.RealValue is > 0 ? Math.Round(settings.Net(totals.RealValue.Value), 2) : null;
            var net = realNet ?? estimatedNet;
            decimal? margin = net != null && product.Price > 0 ? Math.Round((net.Value - product.Price) / product.Price * 100m, 1) : null;

            var verdict = margin == null && product.ContentsFetchedAt == null ? "Carte da leggere"
                : margin == null ? "Non stimabile"
                : margin >= _buyMargin ? "Compra"
                : margin >= 0 ? "Al limite"
                : "Lascia";

            return new SecretLairDropEstimate(
                product.WizardsProductId, product.Price, verdict,
                Math.Round(totals.EstimatedTrend, 2), estimatedNet,
                totals.RealTrend is { } rt ? Math.Round(rt, 2) : null, realNet,
                margin,
                totals.EstimatedValue > 0 ? Math.Round(totals.WeakValue / totals.EstimatedValue * 100m, 1) : 0m,
                totals.UnknownLines,
                product.EstimatedNetValue, product.EstimatedAt,
                product.EstimatedNetValue is { } frozen && realNet is > 0 ? Math.Round((frozen - realNet.Value) / realNet.Value * 100m, 1) : null,
                totals.Cards);
        }

        private sealed record Totals(
            decimal EstimatedTrend, decimal EstimatedValue, decimal WeakValue, int UnknownLines,
            decimal? RealTrend, decimal? RealValue, List<SecretLairCardEstimate> Cards);

        private Totals Compute(SecretLairShopProduct product, int depth)
        {
            if (_cache.TryGetValue(product.Id, out var cached)) return cached;

            decimal trend = 0, value = 0, weak = 0;
            var unknown = 0;
            var cards = new List<SecretLairCardEstimate>();
            decimal? realTrend = 0, realValue = 0;

            foreach (var line in product.Cards.OrderBy(c => c.Id))
            {
                // Un bundle elenca i drop che contiene: si valuta con quelli.
                if (depth == 0
                    && _productsByName.TryGetValue(SecretLairRetrospectiveService.NormalizeDropName(line.CardName), out var child)
                    && child.Id != product.Id)
                {
                    var inner = Compute(child, 1);
                    trend += line.Quantity * inner.EstimatedTrend;
                    value += line.Quantity * inner.EstimatedValue;
                    weak += line.Quantity * inner.WeakValue;
                    unknown += inner.UnknownLines;
                    realTrend = realTrend + line.Quantity * inner.RealTrend;
                    realValue = realValue + line.Quantity * inner.RealValue;
                    cards.Add(new SecretLairCardEstimate(line.Quantity, line.CardName, child.IsFoil, SecretLairCardKind.Product,
                        null, null, inner.EstimatedTrend, inner.RealTrend, Math.Round(inner.EstimatedValue, 2)));
                    continue;
                }

                var (name, foil) = CleanCardLine(line.CardName, product.IsFoil);
                var basePrinting = LookupBase(_model.BaseByName, name);
                var curve = _model.Curve(foil);

                decimal? estimate;
                string kind;
                if (basePrinting != null)
                {
                    estimate = curve.Estimate(basePrinting.Price);
                    kind = SecretLairCardKind.Card;
                }
                else if (!LooksLikeNonCard(name) && curve.NoBasePrice > 0)
                {
                    estimate = curve.NoBasePrice;
                    kind = SecretLairCardKind.NewCard;
                }
                else
                {
                    estimate = null;
                    kind = SecretLairCardKind.Unknown;
                }

                if (estimate is null)
                {
                    unknown++;
                    cards.Add(new SecretLairCardEstimate(line.Quantity, line.CardName, foil, kind, null, null, null, null, null));
                    continue;
                }

                var cardValue = ValueOf(estimate.Value, foil);
                trend += line.Quantity * estimate.Value;
                value += line.Quantity * cardValue;
                if (kind == SecretLairCardKind.NewCard) weak += line.Quantity * cardValue;

                cards.Add(new SecretLairCardEstimate(line.Quantity, line.CardName, foil, kind,
                    basePrinting?.Price, basePrinting?.SetName, estimate, null, Math.Round(cardValue, 2)));
            }

            // Prezzi reali: per un bundle la somma di quelli dei drop contenuti (se li hanno tutti), per
            // gli altri le carte del drop MTGJSON con lo stesso nome, se quasi tutte hanno un prezzo.
            if (!cards.Any(c => c.Kind == SecretLairCardKind.Product))
                (realTrend, realValue) = RealPrices(product, cards);

            var totals = new Totals(trend, value, weak, unknown,
                realTrend is > 0 ? realTrend : null, realValue is > 0 ? realValue : null, cards);
            _cache[product.Id] = totals;
            return totals;
        }

        private (decimal? Trend, decimal? Value) RealPrices(SecretLairShopProduct product, List<SecretLairCardEstimate> cards)
        {
            if (!_model.RealCardsByDropName.TryGetValue(SecretLairRetrospectiveService.NormalizeDropName(product.Title), out var real)
                || real.Count == 0)
                return (null, null);

            var priced = real.Where(c => c.Price != null).ToList();
            if ((decimal)priced.Sum(c => c.Count) / real.Sum(c => c.Count) < MinRealCoverage) return (null, null);

            // Se il negozio elenca le carte, MTGJSON deve averne almeno altrettante: altrimenti il drop
            // su MTGJSON è descritto solo in parte.
            var listed = product.Cards.Sum(c => c.Quantity);
            var listedCards = cards.Where(c => c.Kind is SecretLairCardKind.Card or SecretLairCardKind.NewCard).Sum(c => c.Quantity);
            if (listed > 0 && real.Sum(c => c.Count) < MinRealCoverage * listedCards) return (null, null);

            decimal trend = 0, value = 0;
            foreach (var card in priced)
            {
                trend += card.Count * card.Price!.Value;
                value += card.Count * ValueOf(card.Price.Value, card.Foil);
            }

            // Prezzo reale accanto alla stima, carta per carta, dove il nome corrisponde.
            for (var i = 0; i < cards.Count; i++)
            {
                var key = NormalizeCardName(CleanCardLine(cards[i].Line, product.IsFoil).Name);
                var match = priced.FirstOrDefault(c => NameKeys(c.Name).Contains(key));
                if (match != null) cards[i] = cards[i] with { RealPrice = match.Price };
            }

            return (trend, value);
        }
    }
}

public static class SecretLairCardKind
{
    /// <summary>Carta con altre stampe: stima dalla curva del sovrapprezzo.</summary>
    public const string Card = "Carta";

    /// <summary>Carta senza altre stampe: prezzo mediano delle carte Secret Lair esclusive, stima debole.</summary>
    public const string NewCard = "Senza stampe";

    /// <summary>Drop contenuto in un bundle.</summary>
    public const string Product = "Prodotto";

    /// <summary>Riga non stimabile (carte a sorpresa, gadget, ristampe non elencate).</summary>
    public const string Unknown = "Non stimata";
}

/// <param name="Line">Riga del negozio così com'è.</param>
/// <param name="BasePrice">Trend Cardmarket della stampa più economica.</param>
/// <param name="EstimatedPrice">Prezzo stimato della versione Secret Lair (trend CM).</param>
/// <param name="RealPrice">Trend Cardmarket della versione Secret Lair, quando c'è.</param>
/// <param name="Value">Valore realizzabile lordo di una copia (quota venduta, prezzo realizzato, costo per carta).</param>
public record SecretLairCardEstimate(
    int Quantity,
    string Line,
    bool Foil,
    string Kind,
    decimal? BasePrice,
    string? BaseSet,
    decimal? EstimatedPrice,
    decimal? RealPrice,
    decimal? Value);

/// <param name="Verdict">"Compra", "Al limite", "Lascia", "Non stimabile" o "Carte da leggere".</param>
/// <param name="EstimatedTrend">Somma dei prezzi stimati delle carte (trend CM), al lordo di tutto.</param>
/// <param name="EstimatedNetValue">Valore netto stimato: realizzabile e al netto dei costi di vendita.</param>
/// <param name="RealTrend">Somma dei trend CM reali delle carte, quando il drop ha già prezzi propri.</param>
/// <param name="RealNetValue">Come <paramref name="EstimatedNetValue"/>, ai prezzi reali.</param>
/// <param name="MarginPercent">Valore netto (reale se c'è, altrimenti stimato) contro il prezzo Wizards.</param>
/// <param name="WeakSharePercent">Quota del valore stimato che viene da carte senza stampe precedenti.</param>
/// <param name="UnknownLines">Righe del negozio non stimate.</param>
/// <param name="FrozenNetValue">Stima congelata prima dell'uscita.</param>
/// <param name="FrozenErrorPercent">Scarto della stima congelata dal valore ai prezzi reali.</param>
public record SecretLairDropEstimate(
    string WizardsProductId,
    decimal Price,
    string Verdict,
    decimal EstimatedTrend,
    decimal? EstimatedNetValue,
    decimal? RealTrend,
    decimal? RealNetValue,
    decimal? MarginPercent,
    decimal WeakSharePercent,
    int UnknownLines,
    decimal? FrozenNetValue,
    DateTime? FrozenAt,
    decimal? FrozenErrorPercent,
    List<SecretLairCardEstimate> Cards);

/// <param name="Drops">Drop passati con tutte le carte prezzate su cui si è verificato il modello.</param>
/// <param name="MedianAbsoluteErrorPercent">Scarto tipico fra stima e prezzi reali.</param>
/// <param name="MedianBiasPercent">Positivo = il modello tende a sopravvalutare.</param>
/// <param name="Within25Percent">Quota di drop stimati entro il 25%.</param>
public record SecretLairBacktest(int Drops, decimal? MedianAbsoluteErrorPercent, decimal? MedianBiasPercent, decimal? Within25Percent);

public record SecretLairValuationModel(
    List<SecretLairCurvePoint> NormalCurve,
    List<SecretLairCurvePoint> FoilCurve,
    decimal NoBaseNormalPrice,
    decimal NoBaseFoilPrice,
    int NormalSamples,
    int FoilSamples,
    int NoBaseSamples,
    SecretLairBacktest Backtest,
    decimal BuyMarginPercent,
    OpeningValueSettingsDto Settings);

public record SecretLairValuation(SecretLairValuationModel Model, List<SecretLairDropEstimate> Products);
