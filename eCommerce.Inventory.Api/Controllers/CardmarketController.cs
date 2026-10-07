using eCommerce.Inventory.Api.Models;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace eCommerce.Inventory.Api.Controllers;

/// <summary>
/// Storico prezzi Cardmarket, alimentato dall'import giornaliero del listino pubblico.
/// </summary>
[ApiController]
[Route("api/cardmarket")]
[Authorize]
public class CardmarketController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly CardmarketPriceImportService _importService;
    private readonly ILogger<CardmarketController> _logger;

    public CardmarketController(
        ApplicationDbContext db,
        CardmarketPriceImportService importService,
        ILogger<CardmarketController> logger)
    {
        _db = db;
        _importService = importService;
        _logger = logger;
    }

    /// <summary>
    /// Lancia l'import a mano e ne attende la fine (in genere meno di un minuto). Con
    /// <c>force</c> riscarica anche se il listino non è cambiato, ma un giorno già importato non
    /// viene comunque riscritto.
    /// </summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromQuery] bool force = false, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Import listino Cardmarket lanciato dall'interfaccia (force={Force})", force);
            var log = await _importService.ImportAsync(CardmarketImportTrigger.Manual, force, cancellationToken);
            return Ok(ApiResponse<CardmarketImportLogDto>.SuccessResult(MapLog(log)));
        }
        catch (CardmarketImportAlreadyRunningException ex)
        {
            return Conflict(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    [HttpGet("import/logs")]
    public async Task<IActionResult> GetImportLogs([FromQuery] int take = 20, CancellationToken cancellationToken = default)
    {
        var logs = await _db.CardmarketImportLogs
            .AsNoTracking()
            .OrderByDescending(l => l.StartedAt)
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<List<CardmarketImportLogDto>>.SuccessResult(logs.Select(MapLog).ToList()));
    }

    /// <summary>Serie storica dei prezzi di un prodotto Cardmarket, dal più vecchio al più recente.</summary>
    [HttpGet("products/{idProduct:int}/prices")]
    public async Task<IActionResult> GetPriceHistory(int idProduct, CancellationToken cancellationToken)
    {
        var product = await _db.CardmarketProducts.AsNoTracking()
            .FirstOrDefaultAsync(p => p.IdProduct == idProduct, cancellationToken);
        if (product == null)
            return NotFound(ApiResponse<object>.ErrorResult($"Prodotto Cardmarket {idProduct} non seguito"));

        var prices = await _db.CardmarketPriceSnapshots.AsNoTracking()
            .Where(s => s.IdProduct == idProduct)
            .OrderBy(s => s.Date)
            .Select(s => new CardmarketPricePointDto(s.Date, s.Trend, s.Low, s.Avg, s.Avg7, s.TrendFoil, s.LowFoil))
            .ToListAsync(cancellationToken);

        return Ok(ApiResponse<CardmarketPriceHistoryDto>.SuccessResult(new CardmarketPriceHistoryDto(
            product.IdProduct, product.Name, product.CategoryName, product.IsSingle, prices)));
    }

    private static CardmarketImportLogDto MapLog(CardmarketImportLog l) => new(
        l.Id, l.Trigger.ToString(), l.Outcome.ToString(), l.StartedAt, l.CompletedAt,
        l.SourceCreatedAt, l.SealedProducts, l.SealedSnapshotsWritten, l.SinglesTracked,
        l.SinglesSnapshotsWritten, l.NewProducts, l.Message);
}

public record CardmarketImportLogDto(
    int Id,
    string Trigger,
    string Outcome,
    DateTime StartedAt,
    DateTime? CompletedAt,
    DateTimeOffset? SourceCreatedAt,
    int SealedProducts,
    int SealedSnapshotsWritten,
    int SinglesTracked,
    int SinglesSnapshotsWritten,
    int NewProducts,
    string? Message);

public record CardmarketPricePointDto(
    DateOnly Date,
    decimal? Trend,
    decimal? Low,
    decimal? Avg,
    decimal? Avg7,
    decimal? TrendFoil,
    decimal? LowFoil);

public record CardmarketPriceHistoryDto(
    int IdProduct,
    string Name,
    string CategoryName,
    bool IsSingle,
    List<CardmarketPricePointDto> Prices);
