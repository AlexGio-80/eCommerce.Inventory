using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Registro degli acquisti di prodotti sigillati. Alla registrazione, e di nuovo quando si segna
/// l'apertura, salva la previsione del modello (valore atteso netto per unità con i parametri del
/// momento): è il termine di paragone per tarare il modello sulle vendite reali.
/// </summary>
public class ProductPurchaseService
{
    private readonly ApplicationDbContext _db;
    private readonly SealedProductAnalysisService _analysis;

    public ProductPurchaseService(ApplicationDbContext db, SealedProductAnalysisService analysis)
    {
        _db = db;
        _analysis = analysis;
    }

    public async Task<List<ProductPurchaseDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var purchases = await _db.ProductPurchases.AsNoTracking()
            .Include(p => p.SealedProduct)
            .OrderByDescending(p => p.PurchasedAt ?? DateOnly.FromDateTime(p.CreatedAt))
            .ThenByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        return purchases.Select(Map).ToList();
    }

    /// <exception cref="ArgumentException">Prodotto inesistente o dati non validi.</exception>
    public async Task<ProductPurchaseDto> SaveAsync(int? id, ProductPurchaseInput input, CancellationToken cancellationToken = default)
    {
        if (input.Quantity <= 0) throw new ArgumentException("La quantità deve essere maggiore di zero");
        if (input.UnitPrice < 0) throw new ArgumentException("Il prezzo non può essere negativo");

        var product = await _db.SealedProducts.FirstOrDefaultAsync(p => p.Id == input.SealedProductId, cancellationToken)
                      ?? throw new ArgumentException($"Prodotto sigillato {input.SealedProductId} inesistente");

        ProductPurchase purchase;
        if (id is { } existingId)
        {
            purchase = await _db.ProductPurchases.FirstOrDefaultAsync(p => p.Id == existingId, cancellationToken)
                       ?? throw new ArgumentException($"Acquisto {existingId} inesistente");
        }
        else
        {
            purchase = new ProductPurchase();
            _db.ProductPurchases.Add(purchase);
        }

        // La previsione conta al momento dell'apertura: si rifà quando l'apertura viene segnata o
        // cambia, quando cambia il prodotto, e finché non è stato possibile calcolarla.
        var refreshPrediction = purchase.Id == 0
            || purchase.SealedProductId != input.SealedProductId
            || purchase.OpenedAt != input.OpenedAt
            || purchase.PredictedOpenValueNet == null;

        purchase.SealedProductId = product.Id;
        purchase.Quantity = input.Quantity;
        purchase.UnitPrice = input.UnitPrice;
        purchase.Store = Trim(input.Store, 100);
        purchase.Seller = Trim(input.Seller, 100);
        purchase.PurchasedAt = input.PurchasedAt;
        purchase.OpenedAt = input.OpenedAt;
        purchase.Tag = Trim(input.Tag, 100);
        purchase.Notes = Trim(input.Notes, 1000);

        if (refreshPrediction)
        {
            await PredictAsync(purchase, product, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        purchase.SealedProduct = product;
        return Map(purchase);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var purchase = await _db.ProductPurchases.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (purchase == null) return false;
        _db.ProductPurchases.Remove(purchase);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Valore atteso netto per unità, sui prezzi Cardmarket corretti. Solo se il modello dà una
    /// decisione: con dati incompleti la previsione sarebbe sottostimata e resta vuota.
    /// </summary>
    private async Task PredictAsync(ProductPurchase purchase, SealedProduct product, CancellationToken cancellationToken)
    {
        var set = await _db.MtgjsonSets.AsNoTracking().FirstOrDefaultAsync(s => s.Code == product.SetCode, cancellationToken);
        var mainCode = set?.ParentCode ?? product.SetCode;

        var analysis = await _analysis.AnalyzeAsync(mainCode, null, cancellationToken);
        var row = analysis?.Products.FirstOrDefault(p => p.Id == product.Id);

        var reliable = row is { OpenValueCm: not null } && row.Decision is "Apri" or "Tieni sigillato";
        purchase.PredictedOpenValueNet = reliable ? row!.OpenValueCm : null;
        purchase.PredictionCoverage = reliable ? row!.CoverageCm : null;
        purchase.PredictedAt = reliable ? DateTime.UtcNow : null;
    }

    private static string? Trim(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        return value.Length <= max ? value : value[..max];
    }

    private static ProductPurchaseDto Map(ProductPurchase p) => new(
        p.Id, p.SealedProductId, p.SealedProduct?.Name ?? string.Empty, p.SealedProduct?.SetCode ?? string.Empty,
        p.Quantity, p.UnitPrice, Math.Round(p.Quantity * p.UnitPrice, 2),
        p.Store, p.Seller, p.PurchasedAt, p.OpenedAt, p.Tag, p.Notes,
        p.PredictedOpenValueNet, p.PredictionCoverage, p.PredictedAt,
        p.PredictedOpenValueNet.HasValue ? Math.Round(p.Quantity * p.PredictedOpenValueNet.Value, 2) : null);
}

public record ProductPurchaseInput(
    int SealedProductId,
    int Quantity,
    decimal UnitPrice,
    string? Store,
    string? Seller,
    DateOnly? PurchasedAt,
    DateOnly? OpenedAt,
    string? Tag,
    string? Notes);

public record ProductPurchaseDto(
    int Id,
    int SealedProductId,
    string ProductName,
    string SetCode,
    int Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    string? Store,
    string? Seller,
    DateOnly? PurchasedAt,
    DateOnly? OpenedAt,
    string? Tag,
    string? Notes,
    decimal? PredictedOpenValueNet,
    decimal? PredictionCoverage,
    DateTime? PredictedAt,
    decimal? PredictedTotalNet);
