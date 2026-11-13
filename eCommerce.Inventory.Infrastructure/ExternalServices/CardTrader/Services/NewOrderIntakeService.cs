using eCommerce.Inventory.Application.Commands;
using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.Services;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;

/// <summary>
/// Registra gli ordini letti da Card Trader trattando quelli nuovi come vendite appena fatte.
///
/// Il webhook di Card Trader non è raggiungibile da fuori, quindi le vendite arrivano solo
/// leggendo gli ordini (controllo periodico o pulsante di sincronizzazione). Un ordine nuovo
/// passa dallo stesso percorso del webhook <c>order.create</c>: giacenza scalata, notifica
/// all'interfaccia e riprezzo delle copie rimaste. Tutti gli altri si sincronizzano e basta.
///
/// Non conta come vendita appena fatta:
/// - l'ordine raccolto di Card Trader Zero ("Ct connect" non più "hub_pending"): le sue righe
///   sono vendite già arrivate una per una come ordini "hub_pending";
/// - un ordine pagato da più di <see cref="RecentSaleWindow"/>: chi sincronizza un intervallo
///   di date vecchio non deve scalare giacenze già allineate dall'export.
/// </summary>
public class NewOrderIntakeService
{
    /// <summary>Oltre questa età un ordine pagato mai visto è storico, non una vendita di oggi.</summary>
    public static readonly TimeSpan RecentSaleWindow = TimeSpan.FromDays(2);

    private readonly IApplicationDbContext _context;
    private readonly IMediator _mediator;
    private readonly InventorySyncService _syncService;
    private readonly ILogger<NewOrderIntakeService> _logger;

    public NewOrderIntakeService(
        IApplicationDbContext context,
        IMediator mediator,
        InventorySyncService syncService,
        ILogger<NewOrderIntakeService> logger)
    {
        _context = context;
        _mediator = mediator;
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>Registra gli ordini e restituisce quanti sono stati trattati come vendite nuove.</summary>
    public async Task<int> IngestAsync(List<CardTraderOrderDto> orderDtos, CancellationToken cancellationToken = default)
    {
        if (orderDtos.Count == 0) return 0;

        var ids = orderDtos.Select(o => o.Id).ToList();
        var known = (await _context.Orders
                .AsNoTracking()
                .Where(o => ids.Contains(o.CardTraderOrderId))
                .Select(o => o.CardTraderOrderId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var now = DateTime.UtcNow;
        var sales = orderDtos
            .Where(o => !known.Contains(o.Id) && IsFreshSale(o, now))
            .ToList();
        var others = orderDtos.Except(sales).ToList();

        if (others.Count > 0)
            await _syncService.SyncOrdersAsync(others, cancellationToken);

        foreach (var dto in sales)
        {
            await _mediator.Send(
                new ProcessCardTraderWebhookCommand($"poll-{dto.Id}", "order.create", dto.Id, "live", dto),
                cancellationToken);
        }

        if (sales.Count > 0)
            _logger.LogInformation("Vendite nuove registrate dalla lettura degli ordini: {Count}", sales.Count);

        return sales.Count;
    }

    public static bool IsFreshSale(CardTraderOrderDto order, DateTime utcNow)
    {
        var isCtConnect = string.Equals(order.Buyer?.Username, CtZeroPreparationReconciler.CtConnectBuyer,
            StringComparison.OrdinalIgnoreCase);
        var isHubPending = string.Equals(order.State, CtZeroPreparationReconciler.HubPendingState,
            StringComparison.OrdinalIgnoreCase);

        // Gli ordini "hub_pending" non hanno data: uno mai visto è per forza recente, perché la
        // sincronizzazione notturna legge gli ultimi 7 giorni.
        if (isHubPending) return true;
        if (isCtConnect) return false;

        return order.PaidAt.HasValue && utcNow - order.PaidAt.Value.ToUniversalTime() <= RecentSaleWindow;
    }
}
