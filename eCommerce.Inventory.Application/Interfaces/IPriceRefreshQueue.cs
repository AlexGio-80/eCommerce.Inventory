namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>
/// Coda dei blueprint da riprezzare fuori dal ciclo di richiesta.
///
/// Serve al reprice immediato dopo una vendita: il webhook di Card Trader deve
/// rispondere in fretta, mentre una valutazione di prezzo comporta chiamate API
/// soggette a rate limit. Il webhook accoda e restituisce subito il controllo;
/// il consumo avviene in background.
/// </summary>
public interface IPriceRefreshQueue
{
    /// <summary>Accoda un blueprint da rivalutare. I duplicati già in attesa vengono ignorati.</summary>
    void Enqueue(int blueprintId, string reason);

    /// <summary>Attende il prossimo blueprint da valutare.</summary>
    Task<PriceRefreshRequest> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>Elementi attualmente in attesa.</summary>
    int PendingCount { get; }
}

public record PriceRefreshRequest(int BlueprintId, string Reason);
