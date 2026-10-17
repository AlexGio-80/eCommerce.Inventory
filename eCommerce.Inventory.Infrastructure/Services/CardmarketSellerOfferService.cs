using System.Globalization;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Offerte dei venditori Cardmarket dalle pagine incollate dall'utente (feature 007, Fase 1): abbinamento al
/// listino e ai sigillati, salvataggio per qualche giorno, valutazione di ogni offerta con la classifica di oggi.
///
/// Un nuovo incolla dello stesso venditore sostituisce le sue offerte dei prodotti che contiene e lascia le
/// altre: la pagina 2, o un'altra categoria, si aggiunge alle precedenti.
/// </summary>
public class CardmarketSellerOfferService
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CardmarketSellerOfferService> _logger;

    public CardmarketSellerOfferService(ApplicationDbContext db, IConfiguration configuration, ILogger<CardmarketSellerOfferService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>Giorni per cui un'offerta incollata vale ancora per il pianificatore.</summary>
    public int ValidDays => _configuration.GetValue("Purchasing:Budget:CardmarketOfferDays", 3);

    /// <param name="seller">Venditore indicato dall'utente; se vuoto, quello letto dalla pagina.</param>
    /// <exception cref="ArgumentException">Nessun venditore o nessuna offerta leggibile.</exception>
    public async Task<SellerPasteResult> ImportAsync(string? seller, string text, CancellationToken cancellationToken)
    {
        var page = CardmarketSellerPageParser.Parse(text);
        var sellerName = string.IsNullOrWhiteSpace(seller) ? page.Seller : seller.Trim();
        if (string.IsNullOrWhiteSpace(sellerName))
            throw new ArgumentException("Non trovo il nome del venditore nella pagina: scrivilo nel campo Venditore");
        if (page.Offers.Count == 0)
            throw new ArgumentException("Nessuna offerta nel testo incollato: copia tutta la pagina delle offerte (Ctrl+A, Ctrl+C)");

        var catalog = await CatalogAsync(cancellationToken);
        var matched = new List<(CardmarketPageOffer Offer, int IdProduct)>();
        var unmatched = new List<string>();
        foreach (var offer in page.Offers)
        {
            if (catalog.TryGetValue(CardmarketSellerPageParser.NormalizeName(offer.Name), out var id)) matched.Add((offer, id));
            else unmatched.Add(offer.Name);
        }

        var ids = matched.Select(m => m.IdProduct).Distinct().ToList();
        var old = await _db.CardmarketSellerOffers
            .Where(o => o.Seller == sellerName && ids.Contains(o.IdProduct))
            .ToListAsync(cancellationToken);
        _db.CardmarketSellerOffers.RemoveRange(old);
        var now = DateTime.UtcNow;
        _db.CardmarketSellerOffers.AddRange(matched.Select(m => new CardmarketSellerOffer
        {
            Seller = sellerName,
            IdProduct = m.IdProduct,
            ProductName = Truncate(m.Offer.Name, 400),
            Category = page.Category == null ? null : Truncate(page.Category, 100),
            Price = m.Offer.Price,
            Quantity = m.Offer.Quantity,
            Comment = m.Offer.Comment == null ? null : Truncate(m.Offer.Comment, 500),
            IsDamaged = m.Offer.IsDamaged,
            PastedAt = now
        }));
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Offerte Cardmarket incollate: venditore {Seller}, categoria {Category}, {Matched} abbinate, {Unmatched} non abbinate, filtro inglese {English}",
            sellerName, page.Category, matched.Count, unmatched.Count, page.EnglishFilter);

        var rows = await EvaluateAsync(sellerName, ids, cancellationToken);
        return new SellerPasteResult(sellerName, page.Category, page.EnglishFilter, page.Offers.Count, rows, unmatched);
    }

    /// <summary>Venditori incollati negli ultimi giorni, con quante offerte e quando.</summary>
    public async Task<List<PastedSeller>> GetSellersAsync(CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-ValidDays);
        var rows = await _db.CardmarketSellerOffers.AsNoTracking()
            .Where(o => o.PastedAt >= since)
            .GroupBy(o => o.Seller)
            .Select(g => new { Seller = g.Key, Offers = g.Count(), Last = g.Max(o => o.PastedAt) })
            .ToListAsync(cancellationToken);
        return rows.OrderByDescending(r => r.Last).Select(r => new PastedSeller(r.Seller, r.Offers, r.Last)).ToList();
    }

    public async Task<int> DeleteSellerAsync(string seller, CancellationToken cancellationToken) =>
        await _db.CardmarketSellerOffers.Where(o => o.Seller == seller).ExecuteDeleteAsync(cancellationToken);

    /// <summary>Offerte ancora valide per il pianificatore, non danneggiate, abbinate a un sigillato.</summary>
    public async Task<List<ValidSellerOffer>> GetValidOffersAsync(CancellationToken cancellationToken)
    {
        var since = DateTime.UtcNow.AddDays(-ValidDays);
        return await _db.CardmarketSellerOffers.AsNoTracking()
            .Where(o => o.PastedAt >= since && !o.IsDamaged && o.Quantity > 0 && o.Price > 0)
            .Join(_db.SealedProducts, o => o.IdProduct, p => p.CardmarketId, (o, p) => new ValidSellerOffer(
                o.Id, o.Seller, p.Id, o.Price, o.Quantity, o.PastedAt))
            .ToListAsync(cancellationToken);
    }

    /// <summary>Nome normalizzato del listino (solo sigillati) → idProduct; con due prodotti dallo stesso nome, quello abbinato a un sigillato.</summary>
    private async Task<Dictionary<string, int>> CatalogAsync(CancellationToken cancellationToken)
    {
        var products = await _db.CardmarketProducts.AsNoTracking()
            .Where(p => !p.IsSingle)
            .Select(p => new { p.IdProduct, p.Name, Linked = _db.SealedProducts.Any(s => s.CardmarketId == p.IdProduct) })
            .ToListAsync(cancellationToken);
        return products
            .GroupBy(p => CardmarketSellerPageParser.NormalizeName(p.Name))
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Linked).ThenBy(p => p.IdProduct).First().IdProduct);
    }

    /// <summary>Ogni offerta del venditore a confronto con la classifica di oggi: conviene aprirla, tenerla, o no.</summary>
    private async Task<List<SellerOfferEvaluation>> EvaluateAsync(string seller, List<int> idProducts, CancellationToken cancellationToken)
    {
        var minRoi = _configuration.GetValue("Purchasing:Budget:MinOpenRoiPercent", 10m);
        var maxHoldPremium = _configuration.GetValue("Purchasing:Budget:MaxHoldPremiumPercent", 10m);
        var lastDate = await _db.SealedOpportunities.Select(o => (DateOnly?)o.Date).MaxAsync(cancellationToken);

        var offers = await _db.CardmarketSellerOffers.AsNoTracking()
            .Where(o => o.Seller == seller && idProducts.Contains(o.IdProduct))
            .ToListAsync(cancellationToken);
        var sealedProducts = await _db.SealedProducts.AsNoTracking()
            .Where(p => p.CardmarketId != null && idProducts.Contains(p.CardmarketId.Value))
            .Select(p => new
            {
                p.Id, p.Name, CardmarketId = p.CardmarketId!.Value,
                Opportunity = _db.SealedOpportunities.Where(o => o.SealedProductId == p.Id && o.Date == lastDate)
                    .Select(o => new { o.Decision, o.OpenValueCm, o.CmTrend, o.HoldScore, o.IsHoldCandidate }).FirstOrDefault()
            })
            .ToListAsync(cancellationToken);
        var byCm = sealedProducts.GroupBy(s => s.CardmarketId).ToDictionary(g => g.Key, g => g.First());

        return offers.Select(o =>
        {
            if (!byCm.TryGetValue(o.IdProduct, out var s))
                return new SellerOfferEvaluation(o.Id, o.ProductName, o.Price, o.Quantity, o.Comment, o.IsDamaged, null, null, null, null, null,
                    "Non è nel catalogo dei sigillati");
            var opp = s.Opportunity;
            var roi = opp?.OpenValueCm is > 0 ? Math.Round((opp.OpenValueCm.Value - o.Price) / o.Price * 100, 1) : (decimal?)null;
            var verdict = o.IsDamaged ? "Danneggiato: escluso"
                : opp == null ? "Nessuna valutazione oggi"
                : opp.Decision == "Apri" && roi >= minRoi ? string.Format(Italian, "Da aprire: guadagno {0:0.00} €", opp.OpenValueCm!.Value - o.Price)
                : opp.IsHoldCandidate && opp.CmTrend is > 0 && o.Price <= opp.CmTrend * (1 + maxHoldPremium / 100)
                    ? string.Format(Italian, "Da tenere: punteggio {0:0}, {1} il trend", opp.HoldScore, o.Price <= opp.CmTrend ? "sotto" : "poco sopra")
                : opp.IsHoldCandidate ? "Da tenere, ma caro rispetto al trend"
                : "Non conveniente oggi";
            return new SellerOfferEvaluation(o.Id, o.ProductName, o.Price, o.Quantity, o.Comment, o.IsDamaged,
                s.Id, opp?.CmTrend, opp?.OpenValueCm, roi, opp?.HoldScore, verdict);
        })
        .OrderBy(r => r.Verdict.StartsWith("Da aprire") ? 0 : r.Verdict.StartsWith("Da tenere:") ? 1 : 2)
        .ThenByDescending(r => r.RoiPercent ?? -999)
        .ToList();
    }

    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <param name="EnglishFilter">Il testo mostra il filtro lingua inglese; se no, le offerte potrebbero essere in altre lingue.</param>
/// <param name="OffersRead">Offerte lette nel testo, abbinate o no.</param>
public record SellerPasteResult(
    string Seller, string? Category, bool EnglishFilter, int OffersRead, List<SellerOfferEvaluation> Offers, List<string> Unmatched);

/// <param name="RoiPercent">Resa dell'apertura al prezzo del venditore.</param>
public record SellerOfferEvaluation(
    int OfferId, string Name, decimal Price, int Quantity, string? Comment, bool IsDamaged, int? SealedProductId,
    decimal? CmTrend, decimal? OpenValueNet, decimal? RoiPercent, decimal? HoldScore, string Verdict);

public record PastedSeller(string Seller, int Offers, DateTime PastedAt);

public record ValidSellerOffer(int OfferId, string Seller, int SealedProductId, decimal Price, int Quantity, DateTime PastedAt);
