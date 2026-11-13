using eCommerce.Inventory.Application.Interfaces;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.BackgroundJobs;

/// <summary>
/// Legge gli ordini recenti di Card Trader a intervalli regolari, al posto del webhook che da
/// fuori non è raggiungibile. Senza questo controllo le vendite arrivavano solo con la
/// sincronizzazione notturna o col pulsante, e il riprezzo alla vendita non partiva mai.
///
/// Costa una richiesta a giro sul limite di 20 al minuto. L'intervallo è
/// <c>CardTraderOrders:PollMinutes</c> (predefinito 15, 0 lo spegne).
/// </summary>
public class OrderPollingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrderPollingWorker> _logger;

    public OrderPollingWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<OrderPollingWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = _configuration.GetValue("CardTraderOrders:PollMinutes", 15);
        if (minutes <= 0)
        {
            _logger.LogInformation("Controllo periodico degli ordini spento (CardTraderOrders:PollMinutes = 0)");
            return;
        }

        var interval = TimeSpan.FromMinutes(minutes);
        _logger.LogInformation("Controllo degli ordini Card Trader ogni {Minutes} minuti", minutes);

        // Lascia partire il servizio e le migration prima della prima lettura.
        try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
        catch (OperationCanceledException) { return; }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var api = scope.ServiceProvider.GetRequiredService<ICardTraderApiService>();
                var intake = scope.ServiceProvider.GetRequiredService<NewOrderIntakeService>();

                // Da ieri: il filtro di Card Trader è per giorno, e così una vendita di poco prima
                // della mezzanotte non sfugge.
                var orders = (await api.GetOrdersAsync(DateTime.Today.AddDays(-1), null, stoppingToken))
                    .Cast<CardTraderOrderDto>()
                    .ToList();

                await intake.IngestAsync(orders, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Controllo periodico degli ordini non riuscito, riprovo al prossimo giro");
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
