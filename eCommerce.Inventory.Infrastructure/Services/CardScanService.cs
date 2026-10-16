using System.Text.Json;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Pricing;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// "Scansiona" e "Cerca" della sezione per il telefono (feature 006, Fase 1): dalla foto alle carte
/// del database che le corrispondono, e la scheda di valutazione di una carta.
///
/// La corrispondenza si fa sui dati nostri (set, numero di raccolta, nome inglese o italiano), mai
/// sulla parola dell'IA: una lettura sbagliata dà al più un elenco vuoto o candidati da scartare.
/// </summary>
public class CardScanService
{
    /// <summary>Id di Magic su Card Trader: le sigle dei set si ripetono fra giochi diversi.</summary>
    private const int MagicCardTraderGameId = 1;

    private const int MaxCandidates = 5;

    private readonly ApplicationDbContext _db;
    private readonly ICardImageReader _reader;
    private readonly ScanDailyCounter _counter;
    private readonly ListingPriceSuggestionService _suggestions;
    private readonly IMarketplaceOfferCache _offerCache;
    private readonly ICardTraderApiService _cardTraderApi;
    private readonly IConfiguration _configuration;

    public CardScanService(
        ApplicationDbContext db,
        ICardImageReader reader,
        ScanDailyCounter counter,
        ListingPriceSuggestionService suggestions,
        IMarketplaceOfferCache offerCache,
        ICardTraderApiService cardTraderApi,
        IConfiguration configuration)
    {
        _db = db;
        _reader = reader;
        _counter = counter;
        _suggestions = suggestions;
        _offerCache = offerCache;
        _cardTraderApi = cardTraderApi;
        _configuration = configuration;
    }

    public bool IsConfigured => _reader.IsConfigured;

    public int DailyLimit => _configuration.GetValue("Scan:DailyLimit", 200);

    /// <exception cref="ScanLimitReachedException">Limite giornaliero raggiunto.</exception>
    public async Task<CardScanResultDto> RecognizeAsync(byte[] image, string mediaType, CancellationToken cancellationToken)
    {
        if (!_counter.TryTake(DailyLimit, out var usedToday))
            throw new ScanLimitReachedException(DailyLimit);

        var reading = await _reader.ReadAsync(image, mediaType, cancellationToken);
        var candidates = await FindCandidatesAsync(reading, cancellationToken);

        return new CardScanResultDto
        {
            Reading = reading,
            Candidates = candidates,
            UsedToday = usedToday,
            DailyLimit = DailyLimit
        };
    }

    public async Task<List<ScanCandidateDto>> FindCandidatesAsync(CardReading reading, CancellationToken cancellationToken)
    {
        var names = new[] { reading.Name, reading.PrintedName }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.ToLower())
            .Distinct()
            .ToList();

        var magic = _db.Blueprints.AsNoTracking()
            .Where(b => b.Expansion.Game.CardTraderId == MagicCardTraderGameId);

        var pool = new List<Blueprint>();

        // Stampa esatta: tutte le carte del set, il numero si confronta in memoria perché Card Trader
        // lo salva con gli zeri davanti ("054") e la carta no ("54").
        if (reading.SetCode != null)
        {
            var codes = SetCodeVariants(reading.SetCode);
            pool.AddRange(await magic
                .Include(b => b.Expansion)
                .Where(b => codes.Contains(b.Expansion.Code.ToLower()))
                .ToListAsync(cancellationToken));
        }

        if (names.Count > 0)
        {
            pool.AddRange(await magic
                .Include(b => b.Expansion)
                .Where(b => names.Contains(b.Name.ToLower()) || (b.ItalianName != null && names.Contains(b.ItalianName.ToLower())))
                .OrderByDescending(b => b.Expansion.ReleaseDate)
                .Take(200)
                .ToListAsync(cancellationToken));
        }

        return Rank(reading, pool.DistinctBy(b => b.Id).ToList());
    }

    /// <summary>
    /// Ordina le carte per quanto corrispondono alla lettura. Una carta del set letto entra solo se ha
    /// anche il nome o il numero giusto; a parità vengono prima le uscite più recenti.
    /// </summary>
    public static List<ScanCandidateDto> Rank(CardReading reading, IReadOnlyCollection<Blueprint> pool)
    {
        var names = new[] { reading.Name, reading.PrintedName }
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .ToList();
        var number = NormalizeNumber(reading.CollectorNumber);
        var setCodes = reading.SetCode == null ? new List<string>() : SetCodeVariants(reading.SetCode);

        return pool
            .Select(b =>
            {
                var nameMatch = names.Any(n =>
                    string.Equals(b.Name, n, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(b.ItalianName, n, StringComparison.OrdinalIgnoreCase));
                var code = b.Expansion?.Code?.ToLowerInvariant();
                var setMatch = code != null && setCodes.Contains(code);
                var collector = CollectorNumber(b.FixedProperties);
                var numberMatch = number != null && NormalizeNumber(collector) == number;

                // A parità, la sigla identica a quella stampata (il set base) prima di Collectors e promo.
                var score = (nameMatch ? 4 : 0) + (setMatch ? 2 : 0) + (setMatch && numberMatch ? 3 : 0)
                            + (code == reading.SetCode ? 1 : 0);
                return new { Blueprint = b, Score = score, Exact = setMatch && numberMatch, Collector = collector, NameMatch = nameMatch, SetMatch = setMatch };
            })
            .Where(x => x.NameMatch || x.Exact)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Blueprint.Expansion?.ReleaseDate ?? DateTime.MinValue)
            .ThenBy(x => x.Blueprint.Id)
            .Take(MaxCandidates)
            .Select(x => ToCandidate(x.Blueprint, x.Collector, x.Exact))
            .ToList();
    }

    public static ScanCandidateDto ToCandidate(Blueprint b, string? collector, bool exact) => new()
    {
        BlueprintId = b.Id,
        CardTraderId = b.CardTraderId,
        Name = b.Name,
        ItalianName = b.ItalianName,
        Version = string.IsNullOrWhiteSpace(b.Version) ? null : b.Version,
        ExpansionName = b.Expansion?.Name ?? string.Empty,
        ExpansionCode = b.Expansion?.Code ?? string.Empty,
        CollectorNumber = collector,
        Rarity = b.Rarity,
        ImageUrl = b.ImageUrl,
        ExactPrint = exact
    };

    /// <summary>Scheda di valutazione: prezzo delle regole, mercato Card Trader, listino Cardmarket, mie copie.</summary>
    /// <returns>Null se il blueprint non esiste.</returns>
    public async Task<CardValuationDto?> ValuateAsync(
        int blueprintId, string condition, string language, bool isFoil, CancellationToken cancellationToken)
    {
        var blueprint = await _db.Blueprints.AsNoTracking()
            .Include(b => b.Expansion)
            .FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken);
        if (blueprint == null) return null;

        // Prima il prezzo delle regole: legge le offerte e le lascia in cache per il resto della scheda.
        var rulesPrice = await _suggestions.SuggestAsync(blueprintId, condition, language, isFoil, false, cancellationToken);

        var offers = await _offerCache.GetOrFetchAsync(
            blueprint.CardTraderId,
            MarketplaceOfferCache.ListingWorkflowMaxAge,
            ct => _cardTraderApi.GetMarketplaceProductsAsync(blueprint.CardTraderId, ct),
            cancellationToken);

        var myUserId = _configuration.GetValue<int>("CardTraderApi:UserId", 0);
        var same = SameKind(offers, condition, language, isFoil, myUserId);

        var cmId = PriceRealizationService.FirstCardmarketId(blueprint.CardMarketIds);
        var cm = cmId == null
            ? null
            : await _db.CardmarketLatestPrices.AsNoTracking().FirstOrDefaultAsync(p => p.IdProduct == cmId, cancellationToken);

        var mine = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.BlueprintId == blueprintId && i.Quantity > 0)
            .OrderBy(i => i.ListingPrice)
            .Select(i => new MyListingDto
            {
                Quantity = i.Quantity,
                Price = i.ListingPrice,
                Condition = i.Condition,
                Language = i.Language,
                IsFoil = i.IsFoil
            })
            .ToListAsync(cancellationToken);

        return new CardValuationDto
        {
            Card = ToCandidate(blueprint, CollectorNumber(blueprint.FixedProperties), true),
            Condition = condition,
            Language = language,
            IsFoil = isFoil,
            RulesPrice = rulesPrice,
            CtOffers = same.Count,
            CtLowest = same.Count > 0 ? same.Min(o => o.PriceCents) / 100m : null,
            CtZeroLowest = same.Where(o => o.User?.CanSellViaHub == true).Select(o => (decimal?)o.PriceCents / 100m).Min(),
            CmTrend = cm == null ? null : isFoil ? cm.TrendFoil : cm.Trend,
            CmLow = cm == null ? null : isFoil ? cm.LowFoil : cm.Low,
            CmDate = cm?.Date,
            MyListings = mine
        };
    }

    /// <summary>
    /// Offerte della stessa condizione, lingua e foil, disponibili, esclusi le mie e i prezzi di comodo
    /// sopra 1.000 € (lo stesso tetto della maschera).
    /// </summary>
    public static List<CardTraderMarketplaceProductDto> SameKind(
        IEnumerable<CardTraderMarketplaceProductDto> offers, string condition, string language, bool isFoil, int myUserId)
    {
        return offers.Where(o =>
                o.Quantity > 0 &&
                o.PriceCents > 0 && o.PriceCents <= 100_000 &&
                !o.Graded && !o.OnVacation &&
                (myUserId == 0 || o.User?.Id != myUserId) &&
                o.Properties.IsFoil == isFoil &&
                !o.Properties.IsSigned && !o.Properties.IsAltered &&
                PricingEngine.ConditionMatches(o.Properties.Condition, condition) &&
                PricingEngine.LanguageMatches(o.Properties.Language, language))
            .ToList();
    }

    /// <summary>
    /// Sigle Card Trader dello stesso set stampato. Card Trader mette le versioni speciali (borderless,
    /// extended art, showcase) in un'espansione "Collectors" con la sigla preceduta da "c" ("cafr" per
    /// AFR), le promo con "p" e le prerelease con "pre"; sulla carta invece c'è la sigla del set base.
    /// </summary>
    public static List<string> SetCodeVariants(string setCode)
    {
        var code = setCode.Trim().ToLowerInvariant();
        return new List<string> { code, "c" + code, "p" + code, "pre" + code };
    }

    /// <summary><c>collector_number</c> dalle proprietà fisse del blueprint (JSON).</summary>
    public static string? CollectorNumber(string? fixedProperties)
    {
        if (string.IsNullOrWhiteSpace(fixedProperties)) return null;
        try
        {
            using var doc = JsonDocument.Parse(fixedProperties);
            return doc.RootElement.ValueKind == JsonValueKind.Object &&
                   doc.RootElement.TryGetProperty("collector_number", out var n) &&
                   n.ValueKind == JsonValueKind.String
                ? n.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Numero senza zeri davanti e in minuscolo: "054" e "54" sono lo stesso numero.</summary>
    public static string? NormalizeNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return null;
        var n = number.Trim().ToLowerInvariant();
        var slash = n.IndexOf('/');
        if (slash > 0) n = n[..slash];
        n = n.TrimStart('0');
        return n.Length == 0 ? "0" : n;
    }
}

/// <summary>Riconoscimenti contati per giorno, in memoria: un riavvio azzera il conto, e va bene così.</summary>
public class ScanDailyCounter
{
    private readonly object _lock = new();
    private DateOnly _day;
    private int _count;

    /// <param name="usedToday">Riconoscimenti di oggi compreso questo, se concesso.</param>
    public bool TryTake(int limit, out int usedToday)
    {
        lock (_lock)
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            if (today != _day)
            {
                _day = today;
                _count = 0;
            }

            if (_count >= limit)
            {
                usedToday = _count;
                return false;
            }

            usedToday = ++_count;
            return true;
        }
    }
}

public class ScanLimitReachedException : Exception
{
    public ScanLimitReachedException(int limit)
        : base($"Raggiunto il limite di {limit} riconoscimenti per oggi (Scan:DailyLimit)") { }
}
