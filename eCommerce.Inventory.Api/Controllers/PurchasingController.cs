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
    private readonly OpeningBalanceService _openingBalance;
    private readonly ProductPurchaseService _purchases;
    private readonly SealedOpportunityService _opportunities;
    private readonly AlertService _alerts;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PurchasingController> _logger;

    public PurchasingController(
        SealedProductAnalysisService analysis,
        SealedCatalogImportService catalogImport,
        MtgjsonSetDetailImportService detailImport,
        OpeningBalanceService openingBalance,
        ProductPurchaseService purchases,
        SealedOpportunityService opportunities,
        AlertService alerts,
        IConfiguration configuration,
        ILogger<PurchasingController> logger)
    {
        _alerts = alerts;
        _opportunities = opportunities;
        _openingBalance = openingBalance;
        _purchases = purchases;
        _configuration = configuration;
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
        [FromQuery] decimal? priceRealizationPercent,
        CancellationToken cancellationToken)
    {
        var overrides = new OpeningValueOverrides(bulkThreshold, bulkPrice, bulkSellThroughPercent, sellingCostPercent, priceRealizationPercent);
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

    /// <summary>Bilancio reale di ogni apertura, ricostruito dai tag delle inserzioni.</summary>
    [HttpGet("openings")]
    public async Task<IActionResult> GetOpenings(CancellationToken cancellationToken)
    {
        var threshold = _configuration.GetValue("Purchasing:BulkThreshold", 0.25m);
        var openings = await _openingBalance.GetAsync(threshold, cancellationToken);
        return Ok(ApiResponse<List<OpeningBalance>>.SuccessResult(openings));
    }

    /// <summary>Ultima classifica delle opportunità sui sigillati di tutte le uscite.</summary>
    [HttpGet("opportunities")]
    public async Task<IActionResult> GetOpportunities(CancellationToken cancellationToken)
    {
        var list = await _opportunities.GetLatestAsync(cancellationToken);
        return Ok(ApiResponse<OpportunityList>.SuccessResult(list));
    }

    /// <summary>Ricalcola subito la classifica di oggi (gira comunque da sola ogni giorno).</summary>
    [HttpPost("opportunities/compute")]
    public async Task<IActionResult> ComputeOpportunities(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Ricalcolo della classifica delle opportunità lanciato dall'interfaccia");
            var rows = await _opportunities.ComputeAsync(cancellationToken);
            return Ok(ApiResponse<object>.SuccessResult(new { rows }));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> GetAlertRules(CancellationToken cancellationToken) =>
        Ok(ApiResponse<List<AlertRuleDto>>.SuccessResult(await _alerts.ListRulesAsync(cancellationToken)));

    [HttpPost("alerts")]
    public Task<IActionResult> CreateAlertRule([FromBody] AlertRuleInput input, CancellationToken cancellationToken) =>
        SaveAlertRuleAsync(null, input, cancellationToken);

    [HttpPut("alerts/{id:int}")]
    public Task<IActionResult> UpdateAlertRule(int id, [FromBody] AlertRuleInput input, CancellationToken cancellationToken) =>
        SaveAlertRuleAsync(id, input, cancellationToken);

    [HttpDelete("alerts/{id:int}")]
    public async Task<IActionResult> DeleteAlertRule(int id, CancellationToken cancellationToken) =>
        await _alerts.DeleteRuleAsync(id, cancellationToken)
            ? Ok(ApiResponse<object>.SuccessResult(new { id }))
            : NotFound(ApiResponse<object>.ErrorResult($"Regola {id} inesistente"));

    /// <summary>Valuta subito le regole (gira comunque da sola ogni mattina dopo la classifica).</summary>
    [HttpPost("alerts/evaluate")]
    public async Task<IActionResult> EvaluateAlerts(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _alerts.EvaluateAsync(cancellationToken);
            return Ok(ApiResponse<AlertEvaluationResult>.SuccessResult(result));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    [HttpPost("alerts/test-email")]
    public async Task<IActionResult> SendTestEmail(CancellationToken cancellationToken)
    {
        try
        {
            await _alerts.SendTestEmailAsync(cancellationToken);
            return Ok(ApiResponse<object>.SuccessResult(new { sent = true }));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Email di prova degli avvisi non inviata");
            return BadRequest(ApiResponse<object>.ErrorResult($"Email non inviata: {ex.Message}"));
        }
    }

    [HttpGet("notifications")]
    public async Task<IActionResult> GetNotifications([FromQuery] int take = 50, CancellationToken cancellationToken = default) =>
        Ok(ApiResponse<AlertNotificationList>.SuccessResult(await _alerts.ListNotificationsAsync(take, cancellationToken)));

    /// <summary>Segna come letto un avviso; senza id, tutti.</summary>
    [HttpPost("notifications/read")]
    public async Task<IActionResult> MarkNotificationsRead([FromQuery] int? id, CancellationToken cancellationToken)
    {
        await _alerts.MarkReadAsync(id, cancellationToken);
        return Ok(ApiResponse<object>.SuccessResult(new { id }));
    }

    private async Task<IActionResult> SaveAlertRuleAsync(int? id, AlertRuleInput input, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(ApiResponse<AlertRuleDto>.SuccessResult(await _alerts.SaveRuleAsync(id, input, cancellationToken)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    [HttpGet("purchases")]
    public async Task<IActionResult> GetPurchases(CancellationToken cancellationToken)
    {
        var purchases = await _purchases.ListAsync(cancellationToken);
        return Ok(ApiResponse<List<ProductPurchaseDto>>.SuccessResult(purchases));
    }

    [HttpPost("purchases")]
    public Task<IActionResult> CreatePurchase([FromBody] ProductPurchaseInput input, CancellationToken cancellationToken) =>
        SavePurchaseAsync(null, input, cancellationToken);

    [HttpPut("purchases/{id:int}")]
    public Task<IActionResult> UpdatePurchase(int id, [FromBody] ProductPurchaseInput input, CancellationToken cancellationToken) =>
        SavePurchaseAsync(id, input, cancellationToken);

    [HttpDelete("purchases/{id:int}")]
    public async Task<IActionResult> DeletePurchase(int id, CancellationToken cancellationToken) =>
        await _purchases.DeleteAsync(id, cancellationToken)
            ? Ok(ApiResponse<object>.SuccessResult(new { id }))
            : NotFound(ApiResponse<object>.ErrorResult($"Acquisto {id} inesistente"));

    private async Task<IActionResult> SavePurchaseAsync(int? id, ProductPurchaseInput input, CancellationToken cancellationToken)
    {
        try
        {
            var saved = await _purchases.SaveAsync(id, input, cancellationToken);
            return Ok(ApiResponse<ProductPurchaseDto>.SuccessResult(saved));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.ErrorResult(ex.Message));
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
