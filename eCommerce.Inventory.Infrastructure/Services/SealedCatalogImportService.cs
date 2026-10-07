using System.Globalization;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.MtgJson;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Import del catalogo dei prodotti sigillati da MTGJSON (<c>SetList.json</c>): espansioni,
/// prodotti, contenuto e id Cardmarket/Card Trader. Un solo file per tutte le espansioni, quindi
/// l'import è sempre completo; gira ogni giorno dopo il listino Cardmarket e a richiesta.
///
/// I prodotti spariti da MTGJSON non vengono cancellati: lo storico prezzi Cardmarket resta
/// comunque leggibile, e un prodotto ritirato per errore riapparirebbe all'import successivo.
/// </summary>
public class SealedCatalogImportService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IMtgJsonSetListClient _client;
    private readonly ILogger<SealedCatalogImportService> _logger;

    public SealedCatalogImportService(
        ApplicationDbContext db,
        IMtgJsonSetListClient client,
        ILogger<SealedCatalogImportService> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    /// <exception cref="InvalidOperationException">Se un altro import del catalogo è in corso.</exception>
    public async Task<SealedCatalogImportResult> ImportAsync(CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Un import del catalogo MTGJSON è già in corso");

        try
        {
            var setList = await _client.GetSetListAsync(cancellationToken);
            var result = await SaveAsync(setList, cancellationToken);

            _logger.LogInformation(
                "Catalogo sigillati MTGJSON importato: {Sets} espansioni, {Products} prodotti ({New} nuovi)",
                result.Sets, result.Products, result.NewProducts);

            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<SealedCatalogImportResult> SaveAsync(MtgJsonSetListFile setList, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sets = await _db.MtgjsonSets.ToDictionaryAsync(s => s.Code, cancellationToken);
        var products = await _db.SealedProducts.Include(p => p.Contents).ToDictionaryAsync(p => p.Uuid, cancellationToken);
        var newProducts = 0;
        var productCount = 0;

        foreach (var setDto in setList.Data)
        {
            var code = setDto.Code.ToUpperInvariant();
            if (!sets.TryGetValue(code, out var set))
            {
                set = new MtgjsonSet { Code = code };
                _db.MtgjsonSets.Add(set);
                sets[code] = set;
            }

            set.Name = setDto.Name;
            set.ParentCode = setDto.ParentCode?.ToUpperInvariant();
            set.Type = setDto.Type;
            set.ReleaseDate = DateOnly.TryParseExact(setDto.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var releaseDate) ? releaseDate : null;
            set.LastImportedAt = now;

            foreach (var dto in setDto.SealedProduct ?? new List<MtgJsonSealedProductDto>())
            {
                if (dto.Uuid == Guid.Empty) continue;
                productCount++;

                if (!products.TryGetValue(dto.Uuid, out var product))
                {
                    product = new SealedProduct { Uuid = dto.Uuid };
                    _db.SealedProducts.Add(product);
                    products[dto.Uuid] = product;
                    newProducts++;
                }

                product.SetCode = code;
                product.Name = dto.Name;
                product.Category = dto.Category;
                product.Subtype = dto.Subtype;
                product.CardCount = dto.CardCount;
                product.CardmarketId = ParseId(dto.Identifiers, "mcmId");
                product.CardTraderBlueprintId = ParseId(dto.Identifiers, "cardtraderId");
                product.LastImportedAt = now;

                // Il contenuto si sostituisce per intero: confrontare voce per voce non porta nulla,
                // e così una correzione su MTGJSON arriva così com'è.
                _db.SealedProductContents.RemoveRange(product.Contents);
                product.Contents = MapContents(dto.Contents).ToList();
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new SealedCatalogImportResult(setList.Data.Count, productCount, newProducts);
    }

    public static IEnumerable<SealedProductContent> MapContents(MtgJsonSealedContentsDto? contents)
    {
        if (contents == null) yield break;

        foreach (var pack in contents.Pack ?? new())
            yield return new SealedProductContent
            {
                Kind = SealedContentKind.Pack, PackCode = pack.Code, SetCode = pack.Set?.ToUpperInvariant()
            };

        foreach (var sealedRef in contents.Sealed ?? new())
            yield return new SealedProductContent
            {
                Kind = SealedContentKind.Sealed, Count = Math.Max(1, sealedRef.Count), Name = sealedRef.Name,
                SetCode = sealedRef.Set?.ToUpperInvariant(), ChildUuid = sealedRef.Uuid
            };

        foreach (var deck in contents.Deck ?? new())
            yield return new SealedProductContent
            {
                Kind = SealedContentKind.Deck, Name = deck.Name, SetCode = deck.Set?.ToUpperInvariant()
            };

        foreach (var card in contents.Card ?? new())
            yield return new SealedProductContent
            {
                Kind = SealedContentKind.Card, Name = card.Name, SetCode = card.Set?.ToUpperInvariant(),
                ChildUuid = card.Uuid, Foil = card.Foil
            };

        foreach (var other in contents.Other ?? new())
            yield return new SealedProductContent { Kind = SealedContentKind.Other, Name = other.Name };

        if (contents.Variable is { Count: > 0 })
            yield return new SealedProductContent { Kind = SealedContentKind.Variable, Name = "Contenuto variabile" };
    }

    private static int? ParseId(Dictionary<string, string>? identifiers, string key) =>
        identifiers != null && identifiers.TryGetValue(key, out var value) && int.TryParse(value, out var id) ? id : null;
}

public record SealedCatalogImportResult(int Sets, int Products, int NewProducts);
