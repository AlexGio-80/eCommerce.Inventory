using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Valore atteso dell'apertura di un prodotto sigillato (Fase 2 dell'analisi acquisti).
///
/// Per una busta: per ogni configurazione possibile (con la sua probabilità) e per ogni slot, il
/// valore medio del foglio di stampa da cui si pesca, cioè la somma di probabilità × prezzo delle
/// sue carte. Per mazzi e carte fisse: la somma dei prezzi. Il risultato è al lordo dei costi di
/// vendita; il netto lo applica chi chiama.
///
/// Il bulk non vale il suo prezzo di listino: sotto <see cref="OpeningValueSettings.BulkThreshold"/>
/// una carta si mette in vendita a <see cref="OpeningValueSettings.BulkPrice"/> e se ne vende solo
/// una parte (<see cref="OpeningValueSettings.BulkSellThrough"/>), misurata sulle vendite reali.
/// </summary>
public class OpeningValueCalculator
{
    private readonly Func<Guid, bool, decimal?> _priceOf;
    private readonly OpeningValueSettings _settings;
    private readonly Dictionary<(Guid, bool), decimal?> _cardValueCache = new();
    private readonly Dictionary<string, PackValue> _packCache = new();
    private readonly IReadOnlyDictionary<string, List<BoosterConfig>> _configs;
    private readonly IReadOnlyDictionary<(string PackKey, string Sheet), BoosterSheet> _sheets;
    private readonly IReadOnlyDictionary<string, MtgjsonDeck> _decks;

    /// <param name="priceOf">Prezzo di una carta (uuid MTGJSON, foil), null se sconosciuto.</param>
    /// <param name="configs">Configurazioni per chiave busta ("TRK:play").</param>
    /// <param name="sheets">Fogli per chiave busta e nome foglio.</param>
    /// <param name="decks">Mazzi per chiave "SET:nome".</param>
    public OpeningValueCalculator(
        Func<Guid, bool, decimal?> priceOf,
        OpeningValueSettings settings,
        IReadOnlyDictionary<string, List<BoosterConfig>> configs,
        IReadOnlyDictionary<(string PackKey, string Sheet), BoosterSheet> sheets,
        IReadOnlyDictionary<string, MtgjsonDeck> decks)
    {
        _priceOf = priceOf;
        _settings = settings;
        _configs = configs;
        _sheets = sheets;
        _decks = decks;
    }

    public static string DeckKey(string setCode, string name) => $"{setCode.ToUpperInvariant()}:{name}";

    /// <summary>
    /// Valore realizzabile di una copia: il bulk vale il suo prezzo per la quota venduta, le altre il
    /// prezzo corretto da <see cref="OpeningValueSettings.PriceFactor"/>.
    /// </summary>
    public decimal? CardValue(Guid uuid, bool foil)
    {
        if (_cardValueCache.TryGetValue((uuid, foil), out var cached)) return cached;

        var price = _priceOf(uuid, foil);
        decimal? value = price switch
        {
            null => null,
            _ when price < _settings.BulkThreshold => _settings.BulkPrice * _settings.BulkSellThrough,
            _ => price * _settings.PriceFactor
        };

        _cardValueCache[(uuid, foil)] = value;
        return value;
    }

    /// <summary>Valore atteso di una busta, o null se MTGJSON non ne riporta la composizione.</summary>
    public PackValue? Pack(string packKey)
    {
        if (_packCache.TryGetValue(packKey, out var cached)) return cached;
        if (!_configs.TryGetValue(packKey, out var configs) || configs.Count == 0) return null;

        var sheetValues = new Dictionary<string, SheetValue>();
        SheetValue SheetOf(string name)
        {
            if (sheetValues.TryGetValue(name, out var sv)) return sv;
            sv = _sheets.TryGetValue((packKey, name), out var sheet) ? EvaluateSheet(sheet) : SheetValue.Missing(name);
            sheetValues[name] = sv;
            return sv;
        }

        decimal value = 0, slots = 0, pricedSlots = 0;
        var slotsPerSheet = new Dictionary<string, decimal>();

        foreach (var config in configs)
        {
            var probability = config.TotalWeight > 0 ? (decimal)config.Weight / config.TotalWeight : 0;
            foreach (var slot in config.Slots)
            {
                var sheet = SheetOf(slot.SheetName);
                var expectedSlots = probability * slot.Count;
                value += expectedSlots * sheet.ValuePerSlot;
                slots += expectedSlots;
                pricedSlots += expectedSlots * sheet.PricedShare;
                slotsPerSheet[slot.SheetName] = slotsPerSheet.GetValueOrDefault(slot.SheetName) + expectedSlots;
            }
        }

        var topCards = sheetValues.Values
            .SelectMany(sv => sv.Cards.Select(c => c with { ProbabilityPerPack = c.ProbabilityPerSlot * slotsPerSheet.GetValueOrDefault(sv.Name) }))
            .GroupBy(c => (c.Uuid, c.Foil))
            .Select(g => g.First() with { ProbabilityPerPack = g.Sum(c => c.ProbabilityPerPack) })
            .OrderByDescending(c => c.ProbabilityPerPack * c.Value)
            .Take(10)
            .ToList();

        var result = new PackValue(
            value,
            slots > 0 ? pricedSlots / slots : 0,
            sheetValues.Values
                .Select(sv => new SheetBreakdown(sv.Name, slotsPerSheet.GetValueOrDefault(sv.Name), sv.ValuePerSlot, sv.PricedShare))
                .OrderByDescending(b => b.SlotsPerPack * b.ValuePerSlot)
                .ToList(),
            topCards);

        _packCache[packKey] = result;
        return result;
    }

    /// <summary>
    /// Valore atteso lordo di un prodotto scomposto. Le buste senza composizione e i mazzi non
    /// trovati restano fuori dal valore e abbassano la copertura.
    /// </summary>
    public ProductValue Product(PackComposition composition)
    {
        decimal value = 0, weightTotal = 0, weightPriced = 0;
        var missingPacks = new List<string>();

        foreach (var (packKey, count) in composition.Packs)
        {
            var pack = Pack(packKey);
            if (pack == null)
            {
                missingPacks.Add(packKey);
                weightTotal += count;
                continue;
            }

            value += count * pack.Value;
            weightTotal += count;
            weightPriced += count * pack.PricedShare;
        }

        var deckCards = new List<(Guid Uuid, bool Foil, int Count)>();
        var missingDecks = new List<string>();
        foreach (var (deckKey, count) in composition.Decks)
        {
            if (_decks.TryGetValue(deckKey, out var deck))
                deckCards.AddRange(deck.Cards.Select(c => (c.CardUuid, c.IsFoil, c.Count * count)));
            else
                missingDecks.Add(deckKey);
        }

        deckCards.AddRange(composition.Cards.Select(kv => (kv.Key.Uuid, kv.Key.Foil, kv.Value)));

        // Le carte fisse pesano sulla copertura come uno slot ciascuna, le buste come una busta:
        // non è una media perfetta, ma dice se manca qualcosa di rilevante.
        foreach (var (uuid, foil, count) in deckCards)
        {
            var cardValue = CardValue(uuid, foil);
            weightTotal += count / 15m;
            if (cardValue is null) continue;
            value += count * cardValue.Value;
            weightPriced += count / 15m;
        }

        weightTotal += missingDecks.Count;

        return new ProductValue(value, weightTotal > 0 ? weightPriced / weightTotal : 0, missingPacks, missingDecks);
    }

    private SheetValue EvaluateSheet(BoosterSheet sheet)
    {
        if (sheet.TotalWeight <= 0) return SheetValue.Missing(sheet.Name);

        decimal value = 0, pricedWeight = 0;
        var cards = new List<CardContribution>();

        foreach (var card in sheet.Cards)
        {
            var probability = (decimal)card.Weight / sheet.TotalWeight;
            var cardValue = CardValue(card.CardUuid, sheet.IsFoil);
            if (cardValue is null) continue;

            value += probability * cardValue.Value;
            pricedWeight += card.Weight;
            cards.Add(new CardContribution(card.CardUuid, sheet.IsFoil, cardValue.Value, probability, 0));
        }

        return new SheetValue(sheet.Name, value, pricedWeight / sheet.TotalWeight, cards);
    }

    private record SheetValue(string Name, decimal ValuePerSlot, decimal PricedShare, List<CardContribution> Cards)
    {
        public static SheetValue Missing(string name) => new(name, 0, 0, new List<CardContribution>());
    }
}

/// <param name="PriceFactor">
/// Rapporto fra quanto si incassa davvero e il prezzo usato (vedi <see cref="PriceRealizationService"/>);
/// 1 = nessuna correzione. Non tocca il bulk, che ha le sue regole.
/// </param>
public record OpeningValueSettings(
    decimal BulkThreshold,
    decimal BulkPrice,
    decimal BulkSellThrough,
    decimal SellingCostPercent,
    decimal PriceFactor = 1m)
{
    public decimal Net(decimal gross) => gross * (1 - SellingCostPercent / 100m);
}

/// <param name="Value">Valore atteso lordo della busta.</param>
/// <param name="PricedShare">Quota degli slot coperta da prezzi (1 = tutto prezzato).</param>
public record PackValue(decimal Value, decimal PricedShare, List<SheetBreakdown> Sheets, List<CardContribution> TopCards);

public record SheetBreakdown(string Name, decimal SlotsPerPack, decimal ValuePerSlot, decimal PricedShare);

/// <param name="Value">Valore realizzabile di una copia (bulk già ridotto).</param>
public record CardContribution(Guid Uuid, bool Foil, decimal Value, decimal ProbabilityPerSlot, decimal ProbabilityPerPack);

public record ProductValue(decimal Gross, decimal Coverage, List<string> MissingPacks, List<string> MissingDecks);
