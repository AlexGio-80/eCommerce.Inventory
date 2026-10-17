using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Api.Models;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
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
    private readonly SecretLairRetrospectiveService _secretLair;
    private readonly SecretLairShopMonitorService _secretLairShop;
    private readonly SecretLairDropValuationService _secretLairValuation;
    private readonly ProductPurchaseService _purchases;
    private readonly SealedOpportunityService _opportunities;
    private readonly AlertService _alerts;
    private readonly PurchasePlanService _plans;
    private readonly BudgetPlanService _budgetPlans;
    private readonly BudgetPlanRunner _budgetRunner;
    private readonly CardmarketSellerOfferService _sellerOffers;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PurchasingController> _logger;

    public PurchasingController(
        SealedProductAnalysisService analysis,
        SealedCatalogImportService catalogImport,
        MtgjsonSetDetailImportService detailImport,
        OpeningBalanceService openingBalance,
        SecretLairRetrospectiveService secretLair,
        SecretLairShopMonitorService secretLairShop,
        SecretLairDropValuationService secretLairValuation,
        ProductPurchaseService purchases,
        SealedOpportunityService opportunities,
        AlertService alerts,
        PurchasePlanService plans,
        BudgetPlanService budgetPlans,
        BudgetPlanRunner budgetRunner,
        CardmarketSellerOfferService sellerOffers,
        IConfiguration configuration,
        ILogger<PurchasingController> logger)
    {
        _secretLair = secretLair;
        _secretLairShop = secretLairShop;
        _secretLairValuation = secretLairValuation;
        _plans = plans;
        _budgetPlans = budgetPlans;
        _budgetRunner = budgetRunner;
        _sellerOffers = sellerOffers;
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
        [FromQuery] decimal? costPerCard,
        [FromQuery] decimal? boosterPriceRealizationPercent,
        [FromQuery] decimal? deckPriceRealizationPercent,
        [FromQuery] decimal? secretLairPriceRealizationPercent,
        CancellationToken cancellationToken)
    {
        var overrides = new OpeningValueOverrides(bulkThreshold, bulkPrice, bulkSellThroughPercent, sellingCostPercent, costPerCard,
            boosterPriceRealizationPercent, deckPriceRealizationPercent, secretLairPriceRealizationPercent);
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

    /// <summary>Bilancio dei drop Secret Lair comprati e venduti a singole.</summary>
    [HttpGet("secret-lair/drops")]
    public async Task<IActionResult> GetSecretLairDrops(CancellationToken cancellationToken)
    {
        var retrospective = await _secretLair.GetAsync(cancellationToken);
        return Ok(ApiResponse<SecretLairRetrospective>.SuccessResult(retrospective));
    }

    /// <summary>Prodotti visti nel negozio Secret Lair di Wizards ed esito delle ultime letture.</summary>
    [HttpGet("secret-lair/shop")]
    public async Task<IActionResult> GetSecretLairShop(CancellationToken cancellationToken)
    {
        var view = await _secretLairShop.GetAsync(cancellationToken);
        return Ok(ApiResponse<SecretLairShopView>.SuccessResult(view));
    }

    /// <summary>
    /// Valutazione dei prodotti del negozio Secret Lair: valore stimato dalle stampe esistenti col
    /// sovrapprezzo misurato sui drop passati, confronto col prezzo Wizards e suggerimento.
    /// </summary>
    [HttpGet("secret-lair/valuation")]
    public async Task<IActionResult> GetSecretLairValuation(CancellationToken cancellationToken)
    {
        var valuation = await _secretLairValuation.GetAsync(cancellationToken);
        return Ok(ApiResponse<SecretLairValuation>.SuccessResult(valuation));
    }

    /// <summary>Legge subito il negozio Secret Lair (catalogo e carte dei prodotti nuovi).</summary>
    [HttpPost("secret-lair/shop/refresh")]
    public async Task<IActionResult> RefreshSecretLairShop(CancellationToken cancellationToken)
    {
        try
        {
            var run = await _secretLairShop.RunAsync(cancellationToken);
            return run.Outcome == eCommerce.Inventory.Domain.Entities.SecretLairShopRunOutcome.Succeeded
                ? Ok(ApiResponse<object>.SuccessResult(new { run.Products, run.NewProducts, run.ContentsFetched, run.Message }))
                : StatusCode(502, ApiResponse<object>.ErrorResult(run.Message ?? "Lettura del negozio fallita"));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(ApiResponse<object>.ErrorResult(ex.Message));
        }
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

    /// <summary>
    /// Piano d'acquisto su Card Trader per i prodotti indicati (di solito quelli filtrati nella
    /// classifica delle opportunità): offerte raggruppate per venditore e carrello Card Trader Zero.
    /// Una chiamata a Card Trader per prodotto, quindi qualche decina di secondi.
    /// </summary>
    [HttpPost("purchase-plan")]
    public async Task<IActionResult> BuildPurchasePlan([FromBody] List<int> sealedProductIds, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Piano d'acquisto Card Trader richiesto per {Count} prodotti", sealedProductIds.Count);
            var plan = await _plans.BuildAsync(sealedProductIds, cancellationToken);
            return Ok(ApiResponse<PurchasePlan>.SuccessResult(plan));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    /// <summary>
    /// Mette nel carrello Card Trader dell'account le offerte scelte nel piano d'acquisto. Non compra:
    /// l'acquisto si conclude sul sito. Una chiamata a Card Trader per offerta.
    /// </summary>
    [HttpPost("purchase-plan/cart")]
    public async Task<IActionResult> AddPlanToCart([FromBody] List<PlanCartItem> items, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Carrello Card Trader dal piano: {Count} offerte", items.Count);
            using var interactive = CardTraderRequestPriority.BeginInteractive();
            var result = await _plans.AddToCartAsync(items, cancellationToken);
            return Ok(ApiResponse<PlanCartResult>.SuccessResult(result));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    /// <summary>Contenuto del carrello Card Trader dell'account, per segnare nel piano le offerte già messe.</summary>
    [HttpGet("purchase-plan/cart")]
    public async Task<IActionResult> GetPlanCart(CancellationToken cancellationToken)
    {
        using var interactive = CardTraderRequestPriority.BeginInteractive();
        return Ok(ApiResponse<List<CardTraderCartLine>>.SuccessResult(await _plans.GetCartAsync(cancellationToken)));
    }

    /// <summary>Toglie dal carrello Card Trader le offerte indicate (id delle offerte del marketplace).</summary>
    [HttpPost("purchase-plan/cart/remove")]
    public async Task<IActionResult> RemoveFromPlanCart([FromBody] List<int> cardTraderProductIds, CancellationToken cancellationToken)
    {
        try
        {
            using var interactive = CardTraderRequestPriority.BeginInteractive();
            return Ok(ApiResponse<PlanCartResult>.SuccessResult(await _plans.RemoveFromCartAsync(cardTraderProductIds, cancellationToken)));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    /// <summary>Spedizioni predefinite del pianificatore del budget, da mostrare nella scheda.</summary>
    [HttpGet("budget-plan/settings")]
    public IActionResult GetBudgetPlanSettings() =>
        Ok(ApiResponse<BudgetShipping>.SuccessResult(_budgetPlans.DefaultShipping));

    /// <summary>
    /// Avvia il pianificatore del budget (feature 007): legge le offerte Card Trader dei candidati in
    /// background, 1–2 minuti. Se un calcolo è già in corso restituisce quello.
    /// </summary>
    [HttpPost("budget-plan")]
    public IActionResult StartBudgetPlan([FromBody] BudgetPlanRequest request)
    {
        if (request.Budget <= 0) return BadRequest(ApiResponse<object>.ErrorResult("Indica un budget maggiore di zero"));
        _logger.LogInformation("Pianificatore del budget richiesto: {Budget} €, {Open}% per aprire", request.Budget, request.OpenSharePercent);
        return Ok(ApiResponse<BudgetPlanStatus>.SuccessResult(_budgetRunner.Start(request)));
    }

    [HttpGet("budget-plan/{id}")]
    public IActionResult GetBudgetPlan(string id)
    {
        var status = _budgetRunner.Get(id);
        return status == null
            ? NotFound(ApiResponse<object>.ErrorResult("Piano non trovato: è scaduto o il servizio è ripartito, ricalcolalo"))
            : Ok(ApiResponse<BudgetPlanStatus>.SuccessResult(status));
    }

    /// <summary>Ricalcola il piano sulle offerte già lette, senza Card Trader: righe tolte, spedizioni cambiate.</summary>
    [HttpPost("budget-plan/{id}/recompute")]
    public IActionResult RecomputeBudgetPlan(string id, [FromBody] BudgetRecomputeRequest request)
    {
        var status = _budgetRunner.Recompute(id, request.Shipping, request.Excluded ?? new List<string>(), request.ExcludedStores);
        return status == null
            ? NotFound(ApiResponse<object>.ErrorResult("Piano non trovato: è scaduto o il servizio è ripartito, ricalcolalo"))
            : Ok(ApiResponse<BudgetPlanStatus>.SuccessResult(status));
    }

    /// <summary>
    /// Legge una pagina delle offerte di un venditore Cardmarket incollata dall'utente (feature 007, Fase 1):
    /// abbina le offerte al listino, le salva per il pianificatore e le valuta con la classifica di oggi.
    /// </summary>
    [HttpPost("cardmarket-sellers/paste")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    public async Task<IActionResult> PasteCardmarketSellerPage([FromBody] SellerPasteRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _sellerOffers.ImportAsync(request.Seller, request.Text ?? string.Empty, cancellationToken);
            return Ok(ApiResponse<SellerPasteResult>.SuccessResult(result));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ApiResponse<object>.ErrorResult(ex.Message));
        }
    }

    /// <summary>Venditori Cardmarket incollati negli ultimi giorni.</summary>
    [HttpGet("cardmarket-sellers")]
    public async Task<IActionResult> GetCardmarketSellers(CancellationToken cancellationToken) =>
        Ok(ApiResponse<List<PastedSeller>>.SuccessResult(await _sellerOffers.GetSellersAsync(cancellationToken)));

    [HttpDelete("cardmarket-sellers/{seller}")]
    public async Task<IActionResult> DeleteCardmarketSeller(string seller, CancellationToken cancellationToken) =>
        Ok(ApiResponse<object>.SuccessResult(new { deleted = await _sellerOffers.DeleteSellerAsync(seller, cancellationToken) }));

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

    /// <summary>Magazzino da tenere: acquisti "da tenere" ancora sigillati, con valore di oggi e guadagno latente.</summary>
    [HttpGet("hold-stock")]
    public async Task<IActionResult> GetHoldStock([FromServices] HoldStockService holdStock, CancellationToken cancellationToken)
    {
        var stock = await holdStock.GetAsync(cancellationToken);
        return Ok(ApiResponse<HoldStockDto>.SuccessResult(stock));
    }

    /// <summary>Riscarica l'offerta più bassa su Card Trader dei prodotti in magazzino aggiornati da più di 6 ore.</summary>
    [HttpPost("hold-stock/refresh-cardtrader")]
    public async Task<IActionResult> RefreshHoldStockCardTrader([FromServices] HoldStockService holdStock, CancellationToken cancellationToken)
    {
        var updated = await holdStock.RefreshCardTraderPricesAsync(cancellationToken);
        return Ok(ApiResponse<int>.SuccessResult(updated));
    }

    /// <summary>Prodotti del catalogo sigillati per nome Cardmarket, nome MTGJSON o set (registro acquisti).</summary>
    [HttpGet("catalog/search")]
    public async Task<IActionResult> SearchCatalog([FromQuery] string? q, CancellationToken cancellationToken)
    {
        var products = await _purchases.SearchCatalogAsync(q, cancellationToken);
        return Ok(ApiResponse<List<CatalogProductDto>>.SuccessResult(products));
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

/// <param name="Excluded">Offerte tolte dall'utente (chiavi delle righe del piano).</param>
/// <param name="ExcludedStores">Negozi su cui oggi non si compra ("CardTrader", "Cardmarket", "Wizards").</param>
public record BudgetRecomputeRequest(BudgetShipping? Shipping, List<string>? Excluded, List<string>? ExcludedStores);

/// <param name="Seller">Venditore; vuoto = quello letto dalla pagina.</param>
public record SellerPasteRequest(string? Seller, string? Text);
