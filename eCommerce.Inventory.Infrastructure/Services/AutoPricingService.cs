using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Application.Pricing;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Orchestra l'autopricer: seleziona le carte da valutare, recupera le offerte dal
/// marketplace, invoca il motore e — solo se il profilo non è in dry-run — scrive il
/// nuovo prezzo su Card Trader. Ogni valutazione viene registrata, applicata o meno.
/// </summary>
public class AutoPricingService
{
    private readonly ApplicationDbContext _context;
    private readonly ICardTraderApiService _cardTraderApi;
    private readonly PricingEngine _engine;
    private readonly ILogger<AutoPricingService> _logger;
    private readonly int _myUserId;

    public AutoPricingService(
        ApplicationDbContext context,
        ICardTraderApiService cardTraderApi,
        PricingEngine engine,
        IConfiguration configuration,
        ILogger<AutoPricingService> logger)
    {
        _context = context;
        _cardTraderApi = cardTraderApi;
        _engine = engine;
        _logger = logger;

        // Serve a escludere le proprie inserzioni dal riferimento di mercato.
        _myUserId = configuration.GetValue<int>("CardTraderApi:UserId", 0);
    }

    /// <summary>
    /// Riallinea i prezzi locali a quelli realmente pubblicati su Card Trader.
    ///
    /// Va eseguito prima di ogni valutazione: il prezzo locale determina quale regola si
    /// applica e se scatta il guardrail, quindi un dato stantio porta a decisioni sbagliate
    /// su dati corretti. L'endpoint di export restituisce tutte le inserzioni in una sola
    /// chiamata, perciò l'allineamento costa una richiesta a prescindere dal numero di carte.
    /// </summary>
    /// <returns>Quante righe di inventario sono state corrette.</returns>
    public async Task<int> RefreshLocalPricesFromCardTraderAsync(CancellationToken cancellationToken = default)
    {
        var exported = await _cardTraderApi.GetProductsExportAsync(cancellationToken);

        // Prezzo pubblicato per ogni prodotto Card Trader.
        var remotePrices = new Dictionary<int, decimal>();
        foreach (var product in exported)
        {
            try
            {
                int id = product.Id;
                int priceCents = product.PriceCents;
                remotePrices[id] = priceCents / 100m;
            }
            catch
            {
                // Una riga malformata nell'export non deve impedire l'allineamento delle altre.
            }
        }

        if (remotePrices.Count == 0)
        {
            _logger.LogWarning("L'export di Card Trader non ha restituito prezzi: allineamento saltato");
            return 0;
        }

        var items = await _context.InventoryItems
            .Where(i => i.CardTraderProductId != null)
            .ToListAsync(cancellationToken);

        var corrected = 0;
        foreach (var item in items)
        {
            if (!remotePrices.TryGetValue(item.CardTraderProductId!.Value, out var remotePrice)) continue;
            if (item.ListingPrice == remotePrice) continue;

            _logger.LogDebug(
                "Prezzo locale disallineato per l'articolo {ItemId}: {Local:0.00} € → {Remote:0.00} € (valore reale su Card Trader)",
                item.Id, item.ListingPrice, remotePrice);

            item.ListingPrice = remotePrice;
            corrected++;
        }

        if (corrected > 0)
        {
            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Allineati {Count} prezzi locali ai valori reali di Card Trader", corrected);
        }

        return corrected;
    }

    /// <summary>
    /// Seleziona i blueprint da valutare in una esecuzione notturna.
    ///
    /// Il magazzino conta circa 19.000 blueprint distinti: valutarli tutti a 20 richieste
    /// al minuto richiederebbe 16 ore, ed è verosimilmente il motivo per cui l'autopricer
    /// nativo salta delle carte. La strategia qui è diversa: ogni notte si coprono per
    /// intero le carte di valore, e si aggiunge una fetta a rotazione del bulk scegliendo
    /// quelle ferme da più tempo. Il risultato è una copertura completa e verificabile su
    /// ciò che conta, invece di una copertura casuale su tutto.
    /// </summary>
    /// <param name="highValueThreshold">Sopra questo prezzo la carta viene valutata ogni notte.</param>
    /// <param name="bulkSliceSize">Quante carte bulk aggiungere per esecuzione.</param>
    public async Task<List<int>> SelectBlueprintsForScheduledRunAsync(
        decimal highValueThreshold,
        int bulkSliceSize,
        CancellationToken cancellationToken = default)
    {
        // Prezzo massimo per blueprint: determina se la carta è "di valore".
        var perBlueprint = await _context.InventoryItems
            .AsNoTracking()
            .Where(i => i.Quantity > 0)
            .GroupBy(i => i.BlueprintId)
            .Select(g => new { BlueprintId = g.Key, MaxPrice = g.Max(i => i.ListingPrice) })
            .ToListAsync(cancellationToken);

        var highValue = perBlueprint
            .Where(x => x.MaxPrice > highValueThreshold)
            .Select(x => x.BlueprintId)
            .ToList();

        var bulkIds = perBlueprint
            .Where(x => x.MaxPrice <= highValueThreshold)
            .Select(x => x.BlueprintId)
            .ToList();

        // Ultima valutazione per blueprint: chi non viene toccato da più tempo passa davanti.
        var lastEvaluated = await _context.PriceChangeLogs
            .AsNoTracking()
            .Where(c => bulkIds.Contains(c.BlueprintId))
            .GroupBy(c => c.BlueprintId)
            .Select(g => new { BlueprintId = g.Key, LastAt = g.Max(c => c.CreatedAt) })
            .ToDictionaryAsync(x => x.BlueprintId, x => x.LastAt, cancellationToken);

        var bulkSlice = bulkIds
            .OrderBy(id => lastEvaluated.TryGetValue(id, out var last) ? last : DateTime.MinValue)
            .Take(Math.Max(0, bulkSliceSize))
            .ToList();

        _logger.LogInformation(
            "Selezione autopricer: {HighValue} carte di valore (oltre {Threshold:0.00} €) + {Bulk} bulk a rotazione su {Total} blueprint totali",
            highValue.Count, highValueThreshold, bulkSlice.Count, perBlueprint.Count);

        return highValue.Concat(bulkSlice).ToList();
    }

    /// <summary>
    /// Valuta un insieme di blueprint e restituisce il riepilogo dell'esecuzione.
    /// </summary>
    /// <param name="blueprintIds">Blueprint da valutare, già selezionati dal chiamante.</param>
    /// <param name="profile">Profilo di pricing con regole e filtri.</param>
    /// <param name="trigger">Cosa ha innescato l'esecuzione.</param>
    /// <param name="forceDryRun">Forza la simulazione anche se il profilo è in modalità reale (usato dall'anteprima).</param>
    /// <param name="refreshPricesFirst">Riallinea i prezzi locali a Card Trader prima di valutare.</param>
    /// <param name="forceApply">
    /// Scrive davvero anche se il profilo è in dry-run. È il caso dell'applicazione dall'anteprima:
    /// un gesto esplicito su carte appena esaminate una per una, che è il modo di uscire dalla
    /// simulazione un pezzo alla volta senza aprire la scrittura sull'esecuzione notturna.
    /// Non prevale su <paramref name="forceDryRun"/>: l'anteprima non deve poter scrivere mai.
    /// </param>
    /// <param name="bypassGuardrail">
    /// Ignora il limite di variazione massima per le carte di questa esecuzione. È il caso
    /// di «Applica comunque» dalla scheda Storico, su carte già viste bloccate da <see cref="PricingOutcome.BlockedByGuardrail"/>:
    /// un gesto esplicito, carta per carta, non un cambio del guardrail per tutte le altre.
    /// </param>
    /// <param name="onRunCreated">
    /// Invocato appena la riga di storico esiste a database. Serve a chi segue l'esecuzione
    /// da fuori: la preparazione (selezione delle carte e allineamento dei prezzi) può durare
    /// parecchio, e fino a quel momento non c'è un identificativo da cui leggere l'avanzamento.
    /// </param>
    public async Task<PricingRunLog> RunAsync(
        IReadOnlyList<int> blueprintIds,
        PricingProfile profile,
        PricingTrigger trigger,
        bool forceDryRun = false,
        bool refreshPricesFirst = true,
        CancellationToken cancellationToken = default,
        Action<PricingRunLog>? onRunCreated = null,
        bool forceApply = false,
        bool bypassGuardrail = false)
    {
        var dryRun = forceDryRun || (profile.DryRun && !forceApply);

        if (refreshPricesFirst)
        {
            try
            {
                await RefreshLocalPricesFromCardTraderAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Meglio proseguire con i prezzi locali che rinunciare all'esecuzione:
                // il guardrail resta comunque a protezione delle variazioni anomale.
                _logger.LogWarning(ex, "Allineamento prezzi non riuscito: si prosegue con i valori locali");
            }
        }

        var run = new PricingRunLog
        {
            PricingProfileId = profile.Id,
            Trigger = trigger,
            DryRun = dryRun,
            StartedAt = DateTime.UtcNow,
            PlannedCount = blueprintIds.Count
        };

        // L'anteprima non deve sporcare lo storico delle esecuzioni reali.
        var persistRun = trigger != PricingTrigger.Preview;
        if (persistRun)
        {
            _context.PricingRunLogs.Add(run);
            await _context.SaveChangesAsync(cancellationToken);
            onRunCreated?.Invoke(run);
        }

        _logger.LogInformation(
            "Autopricer avviato | profilo={Profile} blueprint={Count} trigger={Trigger} dryRun={DryRun}",
            profile.Name, blueprintIds.Count, trigger, dryRun);

        if (bypassGuardrail)
        {
            // Va detto a chiaro nel registro: qui il guardrail non protegge, ed è voluto.
            _logger.LogWarning(
                "Guardrail ignorato su richiesta esplicita per {Count} carte (profilo '{Profile}')",
                blueprintIds.Count, profile.Name);
        }

        foreach (var blueprintId in blueprintIds)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                run.ErrorMessage = "Esecuzione interrotta prima del completamento";
                _logger.LogWarning("Autopricer interrotto dopo {Evaluated}/{Planned} blueprint",
                    run.EvaluatedCount, run.PlannedCount);
                break;
            }

            try
            {
                await EvaluateBlueprintAsync(blueprintId, profile, trigger, dryRun, run, persistRun, cancellationToken, bypassGuardrail);
            }
            catch (Exception ex)
            {
                run.FailedCount++;
                _logger.LogError(ex, "Errore nella valutazione del blueprint {BlueprintId}", blueprintId);
            }

            run.EvaluatedCount++;
        }

        run.CompletedAt = DateTime.UtcNow;

        if (persistRun)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Autopricer concluso | valutati={Evaluated}/{Planned} ({Coverage}%) applicati={Applied} " +
            "simulati={Simulated} invariati={NoChange} saltati={Skipped} falliti={Failed} delta={Delta:0.00} €",
            run.EvaluatedCount, run.PlannedCount, run.CoveragePercent, run.AppliedCount,
            run.SimulatedCount, run.NoChangeCount, run.SkippedCount, run.FailedCount, run.TotalPriceDelta);

        return run;
    }

    private async Task EvaluateBlueprintAsync(
        int blueprintId,
        PricingProfile profile,
        PricingTrigger trigger,
        bool dryRun,
        PricingRunLog run,
        bool persistRun,
        CancellationToken cancellationToken,
        bool bypassGuardrail = false)
    {
        var items = await _context.InventoryItems
            .Where(i => i.BlueprintId == blueprintId && i.Quantity > 0)
            .ToListAsync(cancellationToken);

        if (items.Count == 0) return;

        var blueprint = await _context.Blueprints
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == blueprintId, cancellationToken);

        if (blueprint == null)
        {
            _logger.LogWarning("Blueprint {BlueprintId} non trovato a database", blueprintId);
            return;
        }

        // Una sola chiamata API per blueprint, riusata per tutte le mie copie della carta:
        // è la risorsa scarsa, va spesa con parsimonia.
        var offers = (await _cardTraderApi.GetMarketplaceProductsAsync(blueprint.CardTraderId, cancellationToken))
            .ToList();

        foreach (var item in items)
        {
            var decision = _engine.Evaluate(item, offers, profile, _myUserId, bypassGuardrail);

            // Il motore decide in base al profilo; qui si tiene conto anche di come la
            // modalità è stata forzata dal chiamante, in entrambi i versi.
            if (dryRun && decision.Outcome == PricingOutcome.Applied)
            {
                decision.Outcome = PricingOutcome.SimulatedDryRun;
            }
            else if (!dryRun && decision.Outcome == PricingOutcome.SimulatedDryRun)
            {
                // Il motore ha simulato perché il profilo è in dry-run, ma la scrittura è
                // stata chiesta esplicitamente per queste carte: senza questo ramo
                // l'applicazione dall'anteprima non scriverebbe nulla.
                decision.Outcome = PricingOutcome.Applied;
            }

            var log = new PriceChangeLog
            {
                InventoryItemId = item.Id,
                BlueprintId = blueprintId,
                PricingRunLogId = persistRun ? run.Id : null,
                PricingRuleId = decision.RuleId,
                OldPrice = decision.OldPrice,
                ProposedPrice = decision.ProposedPrice,
                ReferencePrice = decision.ReferencePrice,
                ComparableOffersCount = decision.ComparableOffersCount,
                OutliersRejectedCount = decision.OutliersRejectedCount,
                Trigger = trigger,
                Outcome = decision.Outcome,
                Reason = Truncate(decision.Reason, 1000)
            };

            if (decision.Outcome == PricingOutcome.Applied)
            {
                var written = await ApplyPriceAsync(item, decision.ProposedPrice, cancellationToken);
                if (written)
                {
                    run.AppliedCount++;
                    run.TotalPriceDelta += decision.Delta;
                }
                else
                {
                    log.Outcome = PricingOutcome.Failed;
                    log.Reason = Truncate($"Scrittura su Card Trader fallita. {decision.Reason}", 1000);
                    run.FailedCount++;
                }
            }
            else
            {
                switch (decision.Outcome)
                {
                    case PricingOutcome.SimulatedDryRun: run.SimulatedCount++; break;
                    case PricingOutcome.NoChangeNeeded: run.NoChangeCount++; break;
                    default: run.SkippedCount++; break;
                }
            }

            if (persistRun)
            {
                _context.PriceChangeLogs.Add(log);
            }
            else
            {
                // In anteprima il log serve solo come riga di report, non va persistito.
                run.Changes.Add(log);
            }
        }

        if (persistRun)
        {
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Scrive il nuovo prezzo su Card Trader e allinea il record locale.
    /// Il locale si aggiorna solo se la scrittura remota è andata a buon fine,
    /// per non far divergere l'inventario da ciò che vedono i compratori.
    /// </summary>
    private async Task<bool> ApplyPriceAsync(InventoryItem item, decimal newPrice, CancellationToken cancellationToken)
    {
        if (!item.CardTraderProductId.HasValue)
        {
            _logger.LogWarning(
                "InventoryItem {ItemId} non ha CardTraderProductId: impossibile aggiornare il prezzo su Card Trader",
                item.Id);
            return false;
        }

        var ok = await _cardTraderApi.UpdateProductPriceAsync(
            item.CardTraderProductId.Value, newPrice, cancellationToken);

        if (!ok) return false;

        item.ListingPrice = newPrice;
        return true;
    }

    private static string Truncate(string value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
