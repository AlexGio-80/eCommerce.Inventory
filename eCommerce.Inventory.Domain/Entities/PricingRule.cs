namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Come si ricava il prezzo di riferimento dalle offerte comparabili già filtrate.
/// </summary>
public enum PriceReferenceMode
{
    /// <summary>N-esima offerta più bassa: replica il "position" dell'autopricer nativo.</summary>
    NthLowestOffer = 0,

    /// <summary>Offerta più bassa in assoluto.</summary>
    LowestOffer = 1,

    /// <summary>Mediana delle offerte comparabili: robusta, ignora le code.</summary>
    MedianOffer = 2,

    /// <summary>Media aritmetica delle offerte comparabili.</summary>
    AverageOffer = 3,

    /// <summary>Media delle N offerte più basse.</summary>
    AverageOfLowestN = 4,

    /// <summary>
    /// Posizione espressa in percentuale sulla scaletta delle offerte comparabili anziché
    /// come numero d'ordine. Si adatta da sola alla profondità del mercato: sulle carte
    /// osservate le offerte comparabili vanno da 3 a 29, e con un ordinale fisso la stessa
    /// regola significa "stai in fondo" su un mercato profondo e "sii il più caro" su uno
    /// sottile. Con il percentile la collocazione relativa resta quella voluta.
    /// </summary>
    PercentileOffer = 5
}

/// <summary>
/// Regola di pricing valida per una fascia di prezzo. Le fasce non devono sovrapporsi;
/// se lo fanno, vince quella con Priority più bassa.
/// </summary>
public class PricingRule
{
    public int Id { get; set; }

    public int PricingProfileId { get; set; }
    public PricingProfile? PricingProfile { get; set; }

    /// <summary>Estremo inferiore della fascia, incluso. Confrontato con il prezzo corrente della mia carta.</summary>
    public decimal FromPrice { get; set; }

    /// <summary>Estremo superiore della fascia, incluso.</summary>
    public decimal ToPrice { get; set; }

    public PriceReferenceMode ReferenceMode { get; set; } = PriceReferenceMode.NthLowestOffer;

    /// <summary>
    /// Posizione desiderata fra i venditori (1 = il più economico). Usato da
    /// NthLowestOffer e AverageOfLowestN, ignorato dalle altre modalità.
    /// </summary>
    public int Position { get; set; } = 1;

    /// <summary>
    /// Collocazione desiderata sulla scaletta delle offerte comparabili, in percentuale
    /// (0 = la più economica, 100 = la più cara). Usato solo da PercentileOffer.
    /// Con 30 e otto offerte ci si colloca attorno alla terza; con le stesse 30 e venti
    /// offerte attorno alla sesta: la posizione relativa resta la stessa mentre il mercato cambia.
    /// </summary>
    public decimal Percentile { get; set; } = 30m;

    /// <summary>Scostamento fisso in euro applicato al prezzo di riferimento (es. -0,01 per stare un cent sotto).</summary>
    public decimal AdjustmentAmount { get; set; }

    /// <summary>Scostamento percentuale applicato dopo quello fisso (es. -5 per il 5% in meno).</summary>
    public decimal AdjustmentPercent { get; set; }

    /// <summary>Se false, la regola non può mai alzare il prezzo corrente.</summary>
    public bool CanIncrease { get; set; } = true;

    /// <summary>Se false, la regola non può mai abbassare il prezzo corrente.</summary>
    public bool CanDecrease { get; set; } = true;

    /// <summary>A parità di fascia applicabile vince il valore più basso.</summary>
    public int Priority { get; set; }

    public bool IsActive { get; set; } = true;
}
