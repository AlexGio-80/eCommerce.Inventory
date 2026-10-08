using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Bilancio dei drop Secret Lair comprati e venduti a singole: per ogni drop quanto è costato, quanto
/// ha reso e quanto resta in vendita.
///
/// Le carte Secret Lair stanno tutte nella stessa espansione Card Trader e fino a ottobre 2026 avevano
/// lo stesso tag (<c>#SLD_OLD</c>): il drop si ricava abbinando la carta (id Scryfall e foil) al
/// contenuto dei drop su MTGJSON, carte e mazzi. Copre il 93% dell'incassato. Le carte con un tag
/// registrato nel registro acquisti per un drop vanno a quel drop, senza abbinamento.
///
/// Il prezzo pagato non è nei dati: si usa il registro acquisti se il drop è registrato, altrimenti un
/// prezzo standard per tipo (<c>Purchasing:SecretLair:*</c>).
/// </summary>
public class SecretLairRetrospectiveService
{
    private static readonly string[] SecretLairSets = { "SLD", "SLC", "SLU" };

    /// <summary>Sotto queste carte diverse un drop con tutte le carte caricate non prova un acquisto intero.</summary>
    private const int MinCardsForWholeDrop = 3;

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;

    public SecretLairRetrospectiveService(ApplicationDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<SecretLairRetrospective> GetAsync(CancellationToken cancellationToken = default)
    {
        var drops = await _db.SealedProducts.AsNoTracking()
            .Include(p => p.Contents)
            .Where(p => SecretLairSets.Contains(p.SetCode))
            .ToListAsync(cancellationToken);

        // I bundle non si abbinano per carta: le loro carte sono quelle dei drop che contengono, e
        // vanno a quelli. Restano fra i prodotti per gli acquisti registrati con il loro tag.
        var dropCards = await LoadDropCardsAsync(drops.Where(d => TypeOf(d) != SecretLairDropType.Bundle).ToList(), cancellationToken);

        // Carta → drop. Una carta può stare in più drop (ristampe interne): vince il primo, in modo
        // stabile, ed è un caso raro.
        var dropByCard = dropCards
            .GroupBy(c => (c.ScryfallId, c.Foil))
            .ToDictionary(g => g.Key, g => g.OrderBy(c => c.DropId).First().DropId);

        var purchases = await _db.ProductPurchases.AsNoTracking()
            .Where(p => p.SealedProduct != null && SecretLairSets.Contains(p.SealedProduct.SetCode))
            .Select(p => new { p.SealedProductId, p.Quantity, p.UnitPrice, p.Tag })
            .ToListAsync(cancellationToken);
        var dropByTag = purchases
            .Where(p => !string.IsNullOrWhiteSpace(p.Tag))
            .GroupBy(p => OpeningBalanceService.NormalizeTag(p.Tag!))
            .ToDictionary(g => g.Key, g => g.First().SealedProductId);

        var sales = await _db.OrderItems.AsNoTracking()
            .Where(oi => oi.Blueprint != null && oi.Blueprint.Expansion.Name.Contains("Secret Lair") && oi.Order.PaidAt != null)
            .Select(oi => new CardMovement(oi.Blueprint!.ScryfallId, oi.IsFoil, oi.Tag, oi.Quantity, oi.Price, oi.Order.PaidAt!.Value))
            .ToListAsync(cancellationToken);

        var stock = await _db.InventoryItems.AsNoTracking()
            .Where(i => i.Quantity > 0 && i.Blueprint.Expansion.Name.Contains("Secret Lair"))
            .Select(i => new CardMovement(i.Blueprint.ScryfallId, i.IsFoil, i.Tag, i.Quantity, i.ListingPrice, i.DateAdded))
            .ToListAsync(cancellationToken);

        int? DropOf(CardMovement m)
        {
            if (m.Tag != null && dropByTag.TryGetValue(OpeningBalanceService.NormalizeTag(m.Tag), out var tagged)) return tagged;
            return m.ScryfallId != null && dropByCard.TryGetValue((m.ScryfallId, m.IsFoil), out var dropId) ? dropId : null;
        }

        var salesByDrop = sales.GroupBy(DropOf).ToDictionary(g => g.Key ?? 0, g => g.ToList());
        var stockByDrop = stock.GroupBy(DropOf).ToDictionary(g => g.Key ?? 0, g => g.ToList());

        var feeShare = await MeasureFeeShareAsync(cancellationToken);
        var cmValues = await CardmarketValuePerCopyAsync(dropCards, cancellationToken);
        var cardsByDrop = dropCards.GroupBy(c => c.DropId).ToDictionary(g => g.Key, g => g.ToList());

        // Nome su Cardmarket, per il link di ricerca: quello MTGJSON spesso è diverso.
        var cmIds = drops.Where(d => d.CardmarketId.HasValue).Select(d => d.CardmarketId!.Value).ToList();
        var cmNames = await _db.CardmarketProducts.AsNoTracking()
            .Where(p => cmIds.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, p => p.Name, cancellationToken);

        var rows = new List<SecretLairDropRow>();
        var loose = new List<CardMovement>();
        foreach (var drop in drops)
        {
            var dropSales = salesByDrop.GetValueOrDefault(drop.Id) ?? new List<CardMovement>();
            var dropStock = stockByDrop.GetValueOrDefault(drop.Id) ?? new List<CardMovement>();
            if (dropSales.Count == 0 && dropStock.Count == 0) continue;

            var cards = cardsByDrop.GetValueOrDefault(drop.Id) ?? new List<DropCard>();
            var type = TypeOf(drop);
            var registered = purchases.Where(p => p.SealedProductId == drop.Id).ToList();

            // Un drop comprato intero ha tutte le sue carte caricate. Se ne mancano sono singole
            // arrivate con lotti o scambi (utente, 08/10/2026): non hanno il costo di un drop e vanno
            // fra le singole sciolte, salvo che il drop sia nel registro acquisti. Un drop da una o due
            // carte (es. le Astrology Lands) è "completo" per forza e non dimostra niente; i mazzi
            // Commander non si caricano mai interi: entrambi contano solo se registrati.
            var cardsListed = cards.Count(c => dropSales.Any(c.Matches) || dropStock.Any(c.Matches));
            if (registered.Count == 0 && (cards.Count < MinCardsForWholeDrop || cardsListed < cards.Count))
            {
                loose.AddRange(dropSales);
                continue;
            }

            // Copie comprate: ogni copia del drop dà ciascuna delle sue carte, quindi è il minimo di
            // copie (vendute + in vendita) fra le carte del drop. Il massimo conterebbe come drop interi
            // anche le singole sciolte della stessa serie. Il registro acquisti, se c'è, vale di più.
            var estimatedCopies = cards.Count == 0 ? 1 : cards.Min(c =>
                (dropSales.Where(c.Matches).Sum(s => s.Quantity) + dropStock.Where(c.Matches).Sum(s => s.Quantity)) / Math.Max(c.Count, 1));
            estimatedCopies = Math.Max(estimatedCopies, 1);

            var copies = registered.Count > 0 ? registered.Sum(p => p.Quantity) : estimatedCopies;
            var unitPrice = registered.Count > 0 && copies > 0
                ? registered.Sum(p => p.Quantity * p.UnitPrice) / copies
                : StandardPrice(type);
            var cost = copies * unitPrice;

            var gross = dropSales.Sum(s => s.Quantity * s.Price);
            var net = gross * (1 - feeShare);
            var stockValue = dropStock.Sum(s => s.Quantity * s.Price);
            var stockNet = stockValue * (1 - feeShare);
            var firstSeen = dropSales.Select(s => s.Date).Concat(dropStock.Select(s => s.Date)).Min();

            rows.Add(new SecretLairDropRow(
                drop.Id, drop.Name, type, drop.CardmarketId,
                drop.CardmarketId is { } cmId ? cmNames.GetValueOrDefault(cmId) : null,
                drop.CardTraderBlueprintId,
                cards.Count, cardsListed,
                copies, registered.Count > 0, Math.Round(unitPrice, 2), Math.Round(cost, 2),
                dropSales.Sum(s => s.Quantity), Math.Round(gross, 2), Math.Round(net, 2),
                dropStock.Sum(s => s.Quantity), Math.Round(stockValue, 2),
                Math.Round(net - cost, 2),
                Math.Round(net + stockNet - cost, 2),
                cost > 0 ? Math.Round((net + stockNet - cost) / cost * 100m, 1) : null,
                cost > 0 ? Math.Round(net / cost * 100m, 1) : null,
                cmValues.GetValueOrDefault(drop.Id),
                firstSeen));
        }

        var unmatched = salesByDrop.GetValueOrDefault(0) ?? new List<CardMovement>();
        loose.AddRange(unmatched);
        var totalGross = sales.Sum(s => s.Quantity * s.Price);

        var summary = rows
            .GroupBy(r => r.Type)
            .Select(g => new SecretLairTypeSummary(
                g.Key, g.Count(), g.Sum(r => r.Copies), g.Sum(r => r.Cost), g.Sum(r => r.NetRevenue),
                g.Sum(r => r.ProfitWithStock),
                g.Sum(r => r.Cost) > 0 ? Math.Round(g.Sum(r => r.ProfitWithStock) / g.Sum(r => r.Cost) * 100m, 1) : null))
            .OrderBy(s => s.Type)
            .ToList();

        return new SecretLairRetrospective(
            rows.OrderByDescending(r => r.FirstSeen).ToList(),
            summary,
            Math.Round(feeShare * 100m, 2),
            loose.Sum(s => s.Quantity),
            Math.Round(loose.Sum(s => s.Quantity * s.Price), 2),
            totalGross > 0 ? Math.Round((totalGross - unmatched.Sum(s => s.Quantity * s.Price)) / totalGross * 100m, 1) : null,
            StandardPrices());
    }

    /// <summary>Le carte di ogni drop, da carte singole e mazzi MTGJSON, con id Scryfall e foil.</summary>
    private async Task<List<DropCard>> LoadDropCardsAsync(List<SealedProduct> drops, CancellationToken cancellationToken)
    {
        var contents = drops.SelectMany(d => d.Contents.Select(c => (DropId: d.Id, Content: c))).ToList();

        var deckRefs = contents.Where(x => x.Content.Kind == SealedContentKind.Deck && x.Content.Name != null).ToList();
        var deckSets = deckRefs.Select(x => x.Content.SetCode ?? "").Distinct().ToList();
        var decks = (await _db.MtgjsonDecks.AsNoTracking().Include(d => d.Cards)
                .Where(d => deckSets.Contains(d.SetCode)).ToListAsync(cancellationToken))
            .GroupBy(d => (d.SetCode, d.Name))
            .ToDictionary(g => g.Key, g => g.First());

        var raw = new List<(int DropId, Guid Uuid, bool Foil, int Count)>();
        foreach (var (dropId, content) in contents)
        {
            if (content.Kind == SealedContentKind.Card && content.ChildUuid is { } uuid)
                raw.Add((dropId, uuid, content.Foil ?? false, content.Count));
            else if (content.Kind == SealedContentKind.Deck && content.Name != null
                     && decks.TryGetValue((content.SetCode ?? "", content.Name), out var deck))
                raw.AddRange(deck.Cards.Select(c => (dropId, c.CardUuid, c.IsFoil, c.Count * content.Count)));
        }

        var uuids = raw.Select(r => r.Uuid).Distinct().ToList();
        var cards = await _db.MtgjsonCards.AsNoTracking()
            .Where(c => uuids.Contains(c.Uuid))
            .Select(c => new { c.Uuid, c.ScryfallId, c.CardmarketId })
            .ToDictionaryAsync(c => c.Uuid, cancellationToken);

        return raw
            .Where(r => cards.TryGetValue(r.Uuid, out var c) && c.ScryfallId != null)
            .GroupBy(r => (r.DropId, cards[r.Uuid].ScryfallId!, r.Foil))
            .Select(g => new DropCard(g.Key.DropId, g.Key.Item2, g.Key.Foil, g.Sum(r => r.Count), cards[g.First().Uuid].CardmarketId))
            .ToList();
    }

    /// <summary>Valore di una copia del drop aperta, ai trend Cardmarket di oggi delle sue carte.</summary>
    private async Task<Dictionary<int, decimal>> CardmarketValuePerCopyAsync(List<DropCard> cards, CancellationToken cancellationToken)
    {
        var ids = cards.Where(c => c.CardmarketId.HasValue).Select(c => c.CardmarketId!.Value).Distinct().ToList();
        var prices = await _db.CardmarketLatestPrices.AsNoTracking()
            .Where(p => ids.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, cancellationToken);

        return cards
            .GroupBy(c => c.DropId)
            .ToDictionary(g => g.Key, g => Math.Round(g.Sum(c =>
            {
                if (c.CardmarketId is not { } id || !prices.TryGetValue(id, out var p)) return 0m;
                return c.Count * ((c.Foil ? p.TrendFoil : p.Trend) ?? 0m);
            }), 2));
    }

    private async Task<decimal> MeasureFeeShareAsync(CancellationToken cancellationToken)
    {
        var fees = await _db.Orders.AsNoTracking()
            .Where(o => o.PaidAt != null)
            .GroupBy(o => 1)
            .Select(g => new { Fee = g.Sum(o => o.SellerFee), Subtotal = g.Sum(o => o.SellerSubtotal) })
            .FirstOrDefaultAsync(cancellationToken);
        return fees is { Subtotal: > 0 } ? fees.Fee / fees.Subtotal : 0m;
    }

    public static string TypeOf(SealedProduct product) =>
        product.Subtype == "secret_lair_bundle" ? SecretLairDropType.Bundle
        : product.Category == "deck" ? SecretLairDropType.Commander
        : product.Name.Contains("Foil", StringComparison.OrdinalIgnoreCase)
          || product.Name.Contains("Etched", StringComparison.OrdinalIgnoreCase) ? SecretLairDropType.Foil
        : SecretLairDropType.Normal;

    private decimal StandardPrice(string type) => StandardPrices().GetValueOrDefault(type, 34.99m);

    /// <summary>Prezzi Wizards abituali, IVA e spedizione comprese (utente, 08/10/2026).</summary>
    public Dictionary<string, decimal> StandardPrices() => new()
    {
        [SecretLairDropType.Normal] = _configuration.GetValue("Purchasing:SecretLair:NormalPrice", 34.99m),
        [SecretLairDropType.Foil] = _configuration.GetValue("Purchasing:SecretLair:FoilPrice", 44.99m),
        [SecretLairDropType.Bundle] = _configuration.GetValue("Purchasing:SecretLair:BundlePrice", 149m),
        [SecretLairDropType.Commander] = _configuration.GetValue("Purchasing:SecretLair:CommanderPrice", 179m)
    };

    private sealed record CardMovement(string? ScryfallId, bool IsFoil, string? Tag, int Quantity, decimal Price, DateTime Date);

    private sealed record DropCard(int DropId, string ScryfallId, bool Foil, int Count, int? CardmarketId)
    {
        public bool Matches(CardMovement m) => m.ScryfallId == ScryfallId && m.IsFoil == Foil;
    }
}

public static class SecretLairDropType
{
    public const string Normal = "Normale";
    public const string Foil = "Foil";
    public const string Bundle = "Bundle";
    public const string Commander = "Commander";
}

/// <param name="DistinctCards">Carte diverse del drop.</param>
/// <param name="CardsListed">Carte diverse del drop di cui almeno una copia è stata caricata.</param>
/// <param name="RegisteredPurchase">True se copie e prezzo vengono dal registro acquisti, false se stimati.</param>
/// <param name="NetRevenue">Incassato al netto della commissione Card Trader misurata sugli ordini.</param>
/// <param name="StockListingValue">Copie ancora in vendita, al prezzo di listino.</param>
/// <param name="ProfitSoFar">Incassato netto meno spesa.</param>
/// <param name="ProfitWithStock">Come sopra, contando anche le copie in vendita al listino netto.</param>
/// <param name="RecoveredPercent">Quota della spesa già rientrata con le vendite.</param>
/// <param name="CardmarketValuePerCopy">Una copia del drop aperta, ai trend Cardmarket di oggi.</param>
/// <param name="FirstSeen">Prima carta del drop caricata o venduta.</param>
public record SecretLairDropRow(
    int SealedProductId,
    string Name,
    string Type,
    int? CardmarketId,
    string? CardmarketName,
    int? CardTraderBlueprintId,
    int DistinctCards,
    int CardsListed,
    int Copies,
    bool RegisteredPurchase,
    decimal UnitPrice,
    decimal Cost,
    int SoldCopies,
    decimal GrossRevenue,
    decimal NetRevenue,
    int StockCopies,
    decimal StockListingValue,
    decimal ProfitSoFar,
    decimal ProfitWithStock,
    decimal? ReturnPercent,
    decimal? RecoveredPercent,
    decimal CardmarketValuePerCopy,
    DateTime FirstSeen);

public record SecretLairTypeSummary(string Type, int Drops, int Copies, decimal Cost, decimal NetRevenue, decimal ProfitWithStock, decimal? ReturnPercent);

/// <param name="LooseSoldCopies">Copie vendute di singole sciolte: carte di drop non comprati interi, o non ricondotte a nessun drop.</param>
/// <param name="LooseGrossRevenue">Incassato lordo delle singole sciolte.</param>
/// <param name="MatchedRevenuePercent">Quota dell'incassato Secret Lair ricondotta a un drop.</param>
public record SecretLairRetrospective(
    List<SecretLairDropRow> Drops,
    List<SecretLairTypeSummary> Summary,
    decimal CardTraderFeePercent,
    int LooseSoldCopies,
    decimal LooseGrossRevenue,
    decimal? MatchedRevenuePercent,
    Dictionary<string, decimal> StandardPrices);
