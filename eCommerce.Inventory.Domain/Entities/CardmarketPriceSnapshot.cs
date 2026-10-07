namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Prezzi di un prodotto Cardmarket in un giorno, presi dal listino pubblico giornaliero.
///
/// Card Trader e Cardmarket non danno lo storico dei prezzi: l'unico modo di sapere come si muove
/// il prezzo di un box dal preordine in poi è salvarlo noi, giorno per giorno. Quello che non viene
/// salvato oggi non si recupera più.
///
/// I sigillati hanno una riga ogni giorno, così la serie si legge senza ricostruzioni. Le singole
/// solo quando un prezzo cambia, come <see cref="PriceHistoryEntry"/>: per loro conta sapere quando
/// si sono mosse. Su una riga di una singola, quindi, <see cref="Avg1"/>/<see cref="Avg7"/>/<see cref="Avg30"/>
/// sono quelle del giorno del cambio, non di ogni giorno fino al successivo.
/// </summary>
public class CardmarketPriceSnapshot
{
    public int IdProduct { get; set; }

    /// <summary>Giorno del listino (dal suo <c>createdAt</c>), non quello dell'import.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Prezzo medio di vendita.</summary>
    public decimal? Avg { get; set; }

    /// <summary>Prezzo più basso esposto: spesso un venditore lontano o con poche vendite.</summary>
    public decimal? Low { get; set; }

    /// <summary>Andamento calcolato da Cardmarket: è il riferimento scelto per l'acquisto.</summary>
    public decimal? Trend { get; set; }

    public decimal? Avg1 { get; set; }
    public decimal? Avg7 { get; set; }
    public decimal? Avg30 { get; set; }

    // Solo per le singole: sui sigillati Cardmarket li riporta vuoti o a zero.
    public decimal? AvgFoil { get; set; }
    public decimal? LowFoil { get; set; }
    public decimal? TrendFoil { get; set; }
}
