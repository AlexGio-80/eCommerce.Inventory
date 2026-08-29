namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Configurazione di un autopricer: filtri sui venditori di riferimento, guardrail
/// e insieme di regole a fasce di prezzo.
/// </summary>
public class PricingProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }

    /// <summary>
    /// Se true il motore calcola e registra le variazioni ma NON scrive su Card Trader.
    /// È una modalità permanente del profilo, non un'impalcatura temporanea: si spegne
    /// quando le regole sono tarate e si può riaccendere per verificare una modifica.
    /// </summary>
    public bool DryRun { get; set; } = true;

    /// <summary>Nessuna carta verrà mai prezzata sotto questo valore.</summary>
    public decimal MinPrice { get; set; } = 0.05m;

    // --- Guardrail sulle variazioni ---
    // Le due direzioni non comportano lo stesso rischio e non vanno limitate allo stesso modo.
    // Un rialzo sbagliato costa una vendita rimandata: la carta resta a magazzino e l'esecuzione
    // successiva corregge. Un ribasso sbagliato costa la carta, perché viene comprata subito al
    // prezzo sbagliato e non si torna indietro. Di qui la soglia larga in salita e stretta in discesa.

    /// <summary>
    /// Aumento massimo consentito in una singola esecuzione, in percentuale sul prezzo corrente.
    /// Generoso di proposito: serve a cogliere i rialzi reali di mercato, che sono la ragione
    /// principale per cui l'autopricer esiste. La difesa dai prezzi anomali non è affidata a
    /// questa soglia ma ai filtri sulle offerte.
    /// </summary>
    public decimal MaxIncreasePercentPerRun { get; set; } = 300m;

    /// <summary>
    /// Ribasso massimo consentito in una singola esecuzione, in percentuale sul prezzo corrente.
    /// Stretto perché un ribasso eccessivo si traduce in una vendita immediata e irrecuperabile.
    /// </summary>
    public decimal MaxDecreasePercentPerRun { get; set; } = 25m;

    // --- Filtri sui venditori di riferimento ---
    // NB: l'API Card Trader non espone il numero di recensioni di un venditore,
    // quindi un filtro "almeno N feedback" non è realizzabile. Il campo user espone
    // solo: id, username, user_type, country_code, max_sellable_in24h_quantity,
    // one_day_ready, can_sell_via_hub, can_sell_sealed_with_ct_zero, on_vacation.

    public bool IncludeProSellers { get; set; } = true;
    public bool IncludeNormalSellers { get; set; } = true;
    public bool ExcludeVacationSellers { get; set; } = true;

    /// <summary>
    /// Soglia minima su max_sellable_in24h_quantity: proxy della dimensione del venditore,
    /// usato al posto del numero di recensioni che l'API non espone. Null = nessun filtro.
    /// </summary>
    public int? MinSellerDailyCapacity { get; set; }

    /// <summary>Codici paese ISO ammessi, separati da virgola (es. "IT,ES,FR"). Null/vuoto = tutti.</summary>
    public string? CountryCodesCsv { get; set; }

    // --- Scarto degli outlier ---
    // Sostituisce il filtro sulle recensioni: quello che conta non è chi pubblica un prezzo,
    // ma che quel prezzo sia anomalo rispetto al gruppo.

    public bool EnableOutlierRejection { get; set; } = true;

    /// <summary>
    /// Soglia in MAD (Median Absolute Deviation) oltre la quale un'offerta è considerata
    /// anomala. La MAD è preferita alla deviazione standard perché non viene essa stessa
    /// distorta dagli outlier che deve individuare.
    /// </summary>
    public decimal OutlierMadThreshold { get; set; } = 3.0m;

    /// <summary>
    /// Sotto questo numero di offerte comparabili lo scarto statistico non viene applicato.
    /// Tenuto basso di proposito: è sui mercati sottili che un singolo prezzo di comodo fa più
    /// danno, perché con quattro offerte una regola posizionale ci finisce sopra direttamente.
    /// Il caso limite osservato aveva 4 offerte comparabili, di cui una a 1019 € su un mercato
    /// di 73–96 €: con la soglia a 5 lo scarto non partiva proprio.
    /// </summary>
    public int MinOffersForOutlierRejection { get; set; } = 3;

    /// <summary>
    /// Rapporto massimo ammesso fra un'offerta e la mediana delle comparabili, applicato in
    /// entrambe le direzioni (oltre N volte la mediana, o sotto la mediana diviso N).
    /// È un filtro grossolano che però funziona a qualunque numero di offerte, mentre la MAD
    /// richiede qualche punto per essere affidabile. Intercetta le due patologie tipiche del
    /// marketplace: i prezzi di comodo messi altissimi per non sbagliare, e i prezzi
    /// irrealisticamente bassi dei venditori alle prime armi.
    /// Zero o valori minori di 1 disattivano il filtro.
    /// </summary>
    public decimal MaxMedianRatio { get; set; } = 4m;

    /// <summary>
    /// Sotto questo numero di offerte comparabili il prezzo non viene aggiornato affatto:
    /// il mercato è troppo sottile per dedurne un prezzo affidabile.
    /// </summary>
    public int MinComparableOffers { get; set; } = 2;

    /// <summary>
    /// Se true, una regola posizionale non viene applicata quando le offerte comparabili
    /// sono meno della posizione richiesta.
    ///
    /// Serve a evitare due effetti osservati sui dati reali: chiedere "sii il 3° più
    /// economico" dove esistono 2 venditori posiziona la carta sull'offerta più cara, e
    /// se anche l'altro venditore usa un autopricer posizionale i due si rincorrono
    /// verso l'alto senza limite.
    /// </summary>
    public bool SkipWhenFewerOffersThanPosition { get; set; } = true;

    // --- Criteri di comparabilità ---
    // Definiscono quali offerte altrui sono confrontabili con la mia carta.

    public bool MatchCondition { get; set; } = true;
    public bool MatchLanguage { get; set; } = true;
    public bool MatchFoil { get; set; } = true;
    public bool ExcludeSigned { get; set; } = true;
    public bool ExcludeAltered { get; set; } = true;
    public bool ExcludeGraded { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<PricingRule> Rules { get; set; } = new List<PricingRule>();
}
