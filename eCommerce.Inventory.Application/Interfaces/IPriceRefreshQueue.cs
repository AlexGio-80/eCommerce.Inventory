using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>
/// Coda dei blueprint da riprezzare fuori dal ciclo di richiesta.
///
/// Serve a chi deve rispondere in fretta ma vuole comunque una rivalutazione:
/// il webhook di Card Trader dopo una vendita, e la pubblicazione di nuove
/// inserzioni dalla maschera di inserimento. In entrambi i casi la valutazione
/// comporta chiamate API soggette a rate limit: il chiamante accoda e restituisce
/// subito il controllo, il consumo avviene in background.
/// </summary>
public interface IPriceRefreshQueue
{
    /// <summary>Accoda un blueprint da rivalutare. I duplicati già in attesa vengono ignorati.</summary>
    /// <param name="reason">Motivo leggibile, finisce nel log.</param>
    /// <param name="trigger">Origine registrata nello storico delle esecuzioni.</param>
    void Enqueue(int blueprintId, string reason, PricingTrigger trigger);

    /// <summary>Attende il prossimo blueprint da valutare.</summary>
    Task<PriceRefreshRequest> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>Elementi attualmente in attesa.</summary>
    int PendingCount { get; }
}

public record PriceRefreshRequest(int BlueprintId, string Reason, PricingTrigger Trigger);
