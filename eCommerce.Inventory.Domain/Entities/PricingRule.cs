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
    AverageOfLowestN = 4
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
