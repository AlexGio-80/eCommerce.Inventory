using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Punteggio "da tenere" di un sigillato (Fase 1 della feature 005, magazzino da tenere): quanto
/// conviene comprarlo oggi per tenerlo uno-tre anni e rivenderlo, chiuso o aperto.
///
/// Tre componenti, pesate:
/// - <b>pavimento</b>: valore atteso netto dell'apertura / prezzo d'acquisto. Se le carte valgono
///   quasi quanto il prezzo, anche se il sigillato non sale aprendolo si recupera la spesa;
/// - <b>età dell'uscita</b>: i sigillati di solito toccano il minimo fra 6 e 24 mesi dall'uscita e
///   salgono quando escono di stampa; chi è fuori stampa da anni ha di solito già fatto il rialzo;
/// - <b>posizione nello storico</b>: quanto il trend di oggi è vicino al minimo visto per il
///   prodotto. Neutra finché lo storico è troppo corto (parte dal 2026-10-07).
/// </summary>
public static class HoldScoreCalculator
{
    /// <summary>
    /// Tipi valutati: box di buste, bundle, box set e mazzi in ogni confezione. Esclusi buste
    /// singole, prodotti per draft e prerelease, kit: non sono prodotti da tenere.
    /// </summary>
    public static readonly IReadOnlySet<string> Categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "booster_box", "bundle", "box_set", "deck", "deck_box", "subset", "multiple_decks"
    };

    /// <summary>Solo con un prezzo e un valore affidabili il punteggio ha senso.</summary>
    private static readonly IReadOnlySet<string> Decisions = new HashSet<string> { "Apri", "Tieni sigillato" };

    /// <summary>
    /// Secret Lair esclusi per scelta dell'utente: li apre sempre, e hanno la loro scheda con la
    /// valutazione del drop. Il set SLD ha poi la stessa data per tutti i drop, quindi l'età non si
    /// potrebbe nemmeno calcolare.
    /// </summary>
    private static bool IsSecretLair(string? subtype) =>
        subtype is not null && subtype.StartsWith("secret_lair", StringComparison.OrdinalIgnoreCase);

    public static HoldScore? Compute(HoldInput input, HoldSettings settings)
    {
        if (input.Category is null || !Categories.Contains(input.Category) || IsSecretLair(input.Subtype)) return null;
        if (input.Decision is null || !Decisions.Contains(input.Decision)) return null;
        if (input.OpenValueNet is not > 0 || input.PurchasePrice is not > 0) return null;

        var floor = Math.Round(input.OpenValueNet.Value / input.PurchasePrice.Value * 100m, 1);
        int? age = input.ReleaseDate is { } release ? AgeInMonths(release, input.Today) : null;
        var position = HistoryPosition(input.Trend, input.HistoryMin, input.HistoryMax, input.HistoryDays, settings.MinHistoryDays);

        var weighted =
            settings.FloorWeight * FloorPoints(floor)
            + settings.AgeWeight * (age is { } a ? AgePoints(a) : 50m)
            + settings.HistoryWeight * (position ?? 50m);
        var totalWeight = settings.FloorWeight + settings.AgeWeight + settings.HistoryWeight;
        var score = totalWeight > 0 ? Math.Round(weighted / totalWeight, 1) : 0m;

        // Chi rende già aprendolo è un'occasione "compra e apri", nella lista Apri e nei suoi avvisi:
        // da tenere sono i prodotti quasi coperti dalle carte che oggi non conviene ancora aprire.
        var candidate = score >= settings.MinScore
            && floor >= settings.MinFloorPercent
            && (input.OpeningRoiPercent is not { } roi || roi < settings.MaxOpeningRoiPercent)
            && (age is null || (age >= settings.MinAgeMonths && age <= settings.MaxAgeMonths));

        return new HoldScore(score, floor, age, position, candidate);
    }

    /// <summary>Mesi compiuti dall'uscita; negativo per un prodotto in preordine.</summary>
    public static int AgeInMonths(DateOnly release, DateOnly today) =>
        (today.Year - release.Year) * 12 + today.Month - release.Month - (today.Day < release.Day ? 1 : 0);

    /// <summary>0 punti al 60% o sotto, 100 dal 100% in su, lineare in mezzo.</summary>
    public static decimal FloorPoints(decimal floorPercent) => Clamp((floorPercent - 60m) / 40m * 100m);

    /// <summary>
    /// 0 sotto i 6 mesi (il prezzo di solito sta ancora scendendo), a salire fino a 12, pieno fra 12
    /// e 30, a scendere fino a 0 a 60 mesi.
    /// </summary>
    public static decimal AgePoints(int months) => months switch
    {
        < 6 => 0m,
        < 12 => Math.Round((months - 6) / 6m * 100m, 1),
        <= 30 => 100m,
        < 60 => Math.Round((60 - months) / 30m * 100m, 1),
        _ => 0m
    };

    /// <summary>100 = trend al minimo dello storico, 0 = al massimo; null se lo storico è troppo corto.</summary>
    public static decimal? HistoryPosition(decimal? trend, decimal? min, decimal? max, int historyDays, int minHistoryDays)
    {
        if (historyDays < minHistoryDays || trend is not > 0 || min is not { } lo || max is not { } hi) return null;
        if (hi <= lo) return 50m;
        return Math.Round(Clamp((hi - trend.Value) / (hi - lo) * 100m), 1);
    }

    private static decimal Clamp(decimal value) => Math.Min(100m, Math.Max(0m, value));
}

/// <param name="HistoryDays">Giorni fra la prima rilevazione del prodotto nello storico e oggi.</param>
public record HoldInput(
    string? Category,
    string? Subtype,
    string? Decision,
    decimal? OpenValueNet,
    decimal? PurchasePrice,
    decimal? Trend,
    DateOnly? ReleaseDate,
    DateOnly Today,
    decimal? HistoryMin,
    decimal? HistoryMax,
    int HistoryDays,
    decimal? OpeningRoiPercent = null);

/// <param name="Score">0–100.</param>
/// <param name="FloorPercent">Valore atteso netto / prezzo d'acquisto × 100.</param>
/// <param name="AgeMonths">Null senza data d'uscita: conta come neutra.</param>
/// <param name="HistoryPosition">Null finché lo storico del prodotto è troppo corto.</param>
public record HoldScore(decimal Score, decimal FloorPercent, int? AgeMonths, decimal? HistoryPosition, bool IsCandidate);

/// <summary>Pesi e soglie da <c>Purchasing:Hold:*</c>, tarabili senza ripubblicare.</summary>
public record HoldSettings(
    decimal FloorWeight = 50m,
    decimal AgeWeight = 30m,
    decimal HistoryWeight = 20m,
    decimal MinScore = 60m,
    decimal MinFloorPercent = 75m,
    int MinAgeMonths = 6,
    int MaxAgeMonths = 48,
    int MinHistoryDays = 60,
    decimal MaxOpeningRoiPercent = 0m)
{
    public static HoldSettings From(IConfiguration configuration)
    {
        var d = new HoldSettings();
        return new HoldSettings(
            configuration.GetValue("Purchasing:Hold:FloorWeight", d.FloorWeight),
            configuration.GetValue("Purchasing:Hold:AgeWeight", d.AgeWeight),
            configuration.GetValue("Purchasing:Hold:HistoryWeight", d.HistoryWeight),
            configuration.GetValue("Purchasing:Hold:MinScore", d.MinScore),
            configuration.GetValue("Purchasing:Hold:MinFloorPercent", d.MinFloorPercent),
            configuration.GetValue("Purchasing:Hold:MinAgeMonths", d.MinAgeMonths),
            configuration.GetValue("Purchasing:Hold:MaxAgeMonths", d.MaxAgeMonths),
            configuration.GetValue("Purchasing:Hold:MinHistoryDays", d.MinHistoryDays),
            configuration.GetValue("Purchasing:Hold:MaxOpeningRoiPercent", d.MaxOpeningRoiPercent));
    }
}
