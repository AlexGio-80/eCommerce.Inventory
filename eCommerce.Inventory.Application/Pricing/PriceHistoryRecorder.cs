using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Pricing;

/// <summary>
/// Decide quali rilevazioni di prezzo vanno scritte a storico. È separato dalla persistenza
/// perché la regola — cosa costituisce un cambiamento degno di essere registrato — è l'unica
/// parte che può sbagliare, e va poterla verificare senza database né rete.
/// </summary>
public static class PriceHistoryRecorder
{
    /// <summary>Stato di un'inserzione al momento della rilevazione.</summary>
    public readonly record struct Observation(
        int CardTraderProductId,
        int BlueprintId,
        int? InventoryItemId,
        decimal Price,
        int Quantity,
        string Condition,
        string Language,
        bool IsFoil);

    /// <summary>Prezzo e quantità dell'ultima rilevazione conosciuta di un'inserzione.</summary>
    public readonly record struct PreviousState(decimal Price, int Quantity);

    /// <summary>
    /// Rilevazioni da scrivere: quelle il cui prezzo o quantità differiscono dallo stato
    /// precedente, e quelle di inserzioni mai viste prima.
    ///
    /// Il primo punto va registrato anche quando nulla è cambiato, altrimenti un'inserzione dal
    /// prezzo stabile non comparirebbe affatto nello storico e sarebbe indistinguibile da una di
    /// cui non si sa nulla. Serve un valore iniziale da cui far partire la linea.
    /// </summary>
    public static List<PriceHistoryEntry> SelectEntriesToRecord(
        IReadOnlyCollection<Observation> observations,
        IReadOnlyDictionary<int, PreviousState> previousByProductId,
        DateTime recordedAt)
    {
        var entries = new List<PriceHistoryEntry>();

        foreach (var observation in observations)
        {
            if (previousByProductId.TryGetValue(observation.CardTraderProductId, out var previous)
                && previous.Price == observation.Price
                && previous.Quantity == observation.Quantity)
            {
                continue;
            }

            entries.Add(new PriceHistoryEntry
            {
                BlueprintId = observation.BlueprintId,
                InventoryItemId = observation.InventoryItemId,
                CardTraderProductId = observation.CardTraderProductId,
                Price = observation.Price,
                Quantity = observation.Quantity,
                Condition = observation.Condition,
                Language = observation.Language,
                IsFoil = observation.IsFoil,
                RecordedAt = recordedAt
            });
        }

        return entries;
    }
}
