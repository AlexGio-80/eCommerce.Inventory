namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Punto della serie storica del prezzo di una mia inserzione.
///
/// Viene scritto dalla sincronizzazione notturna, che scarica comunque l'export completo con
/// tutti i prezzi: registrare da lì significa avere una serie regolare su tutte le carte, e non
/// solo su quelle che l'autopricer ha toccato quella notte. Cattura inoltre i cambi fatti a mano
/// e quelli dell'autopricer nativo di Card Trader, che altrimenti resterebbero invisibili.
///
/// La serie è a delta: una riga esiste solo quando prezzo o quantità cambiano rispetto alla
/// rilevazione precedente, più un primo punto per ogni inserzione. Un magazzino di 35.000
/// inserzioni scritto per intero ogni notte produrrebbe milioni di righe l'anno per rappresentare
/// in gran parte prezzi fermi; per ricostruire l'andamento basta sapere quando è cambiato.
///
/// Da non confondere con <see cref="PriceChangeLog"/>: quello registra cosa ha deciso
/// l'autopricer e con quale motivazione, e porta anche il riferimento di mercato. Questo registra
/// il prezzo effettivamente esposto, indipendentemente da chi lo ha cambiato.
/// </summary>
public class PriceHistoryEntry
{
    public int Id { get; set; }

    /// <summary>Carta a cui la rilevazione si riferisce. Resta valida anche se l'inserzione sparisce.</summary>
    public int BlueprintId { get; set; }
    public Blueprint? Blueprint { get; set; }

    /// <summary>
    /// Inserzione a magazzino. Diventa null quando la carta viene venduta e la sincronizzazione
    /// la rimuove: la serie storica deve sopravvivere all'inserzione, altrimenti si perde proprio
    /// l'andamento delle carte che sono state vendute.
    /// </summary>
    public int? InventoryItemId { get; set; }
    public InventoryItem? InventoryItem { get; set; }

    /// <summary>Identificativo dell'inserzione su Card Trader: è la chiave stabile della serie.</summary>
    public int CardTraderProductId { get; set; }

    /// <summary>Prezzo esposto, nella stessa scala dell'export: quello che incasso io.</summary>
    public decimal Price { get; set; }

    /// <summary>
    /// Copie disponibili al momento della rilevazione. Messa in grafico accanto al prezzo mostra
    /// quando una carta è stata comprata, che è il contesto che spiega i movimenti di prezzo.
    /// </summary>
    public int Quantity { get; set; }

    // Caratteristiche denormalizzate: la stessa carta esiste in più versioni, e senza queste una
    // serie per blueprint mescolerebbe la foil con la non foil. Copiate qui perché la riga resti
    // leggibile anche dopo che l'inserzione è stata cancellata.
    public string Condition { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public bool IsFoil { get; set; }

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;
}
