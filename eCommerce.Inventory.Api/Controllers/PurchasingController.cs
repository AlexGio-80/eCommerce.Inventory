using eCommerce.Inventory.Api.Models;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eCommerce.Inventory.Api.Controllers;

/// <summary>
/// Analisi acquisto prodotti sigillati: quale formato conviene per ciascuna uscita.
/// Vedi <c>Documentation/Features/004-AnalisiAcquistoProdotti.md</c>.
/// </summary>
[ApiController]
[Route("api/purchasing")]
[Authorize]
public class PurchasingController : ControllerBase
{
    private readonly SealedProductAnalysisService _analysis;
    private readonly SealedCatalogImportService _catalogImport;
    private readonly MtgjsonSetDetailImportService _detailImport;
    private readonly ILogger<PurchasingController> _logger;

    public PurchasingController(
        SealedProductAnalysisService analysis,
        SealedCatalogImportService catalogImport,
        MtgjsonSetDetailImportService detailImport,
        ILogger<PurchasingController> logger)
    {
        _analysis = analysis;
        _catalogImport = catalogImport;
        _detailImport = detailImport;
        _logger = logger;
    }

    [HttpGet("sets")]
    public async Task<IActionResult> GetSets(CancellationToken cancellationToken)
    {
        var sets = await _analysis.GetSetsAsync(cancellationToken);
        return Ok(ApiResponse<List<SealedSetOption>>.SuccessResult(sets));
    }

    /// <summary>
    /// Analisi di un'uscita. I parametri del valore atteso sono facoltativi: senza, valgono la
    /// configurazione (<c>Purchasing:*</c>) e la quota di bulk venduto misurata sulle vendite.
    /// </summary>
    [HttpGet("sets/{code}/analysis")]
    public async Task<IActionResult> GetAnalysis(
        string code,
        [FromQuery] decimal? bulkThreshold,
        [FromQuery] decimal? bulkPrice,
        [FromQuery] decimal? bulkSellThroughPercent,
        [FromQuery] decimal? sellingCostPercent,
        CancellationToken cancellationToken)
    {
        var overrides = new OpeningValueOverrides(bulkThreshold, bulkPrice, bulkSellThroughPercent, sellingCostPercent);
        var analysis = await _analysis.AnalyzeAsync(code, overrides, cancellationToken);
        return analysis == null
            ? NotFound(ApiResponse<object>.ErrorResult($"Espansione {code} non presente nel catalogo MTGJSON"))
            : Ok(ApiResponse<SealedSetAnalysis>.SuccessResult(analysis));
    }

    /// <summary>Chiede a Card Trader i prezzi attuali dei sigillati dell'uscita (poche chiamate).</summary>
    [HttpPost("sets/{code}/cardtrader-prices")]
    public async Task<IActionResult> RefreshCardTraderPrices(string code, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Aggiornamento prezzi Card Trader dei sigillati di {Set} lanciato dall'interfaccia", code);
        var result = await _analysis.RefreshCardTraderPricesAsync(code, cancellationToken);
        return Ok(ApiResponse<CardTraderSealedRefreshResult>.SuccessResult(result));
    }

    /// <summary>Scarica da MTGJSON carte, composizione delle buste e mazzi dei set dell'uscita.</summary>
    [HttpPost("sets/{code}/details/import")]
    public async Task<IActionResult> ImportDetails(string code, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Import dati delle buste MTGJSON di {Set} lanciato dall'interfaccia", code);
            var result = await _detailImport.ImportGroupAsync(code, cancellationToken);
            return Ok(ApiResponse<SetDetailImportResult>.SuccessResult(result));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    [HttpPost("catalog/import")]
    public async Task<IActionResult> ImportCatalog(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Import catalogo sigillati MTGJSON lanciato dall'interfaccia");
            var result = await _catalogImport.ImportAsync(cancellationToken);
            return Ok(ApiResponse<SealedCatalogImportResult>.SuccessResult(result));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }
}
