namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Quanto vale una carta in versione Secret Lair, dato il prezzo della sua stampa più economica.
///
/// Misurato sui drop passati: le carte si ordinano per prezzo della stampa base e si dividono in
/// fasce con lo stesso numero di carte; ogni fascia dà un punto (mediana del prezzo base, mediana
/// del prezzo Secret Lair) e fra un punto e l'altro si interpola. Non è un rapporto fisso: al
/// 09/10/2026 una carta da pochi centesimi in versione SL vale comunque 4-5 € (il "pavimento"),
/// mentre sopra i 15 € il sovrapprezzo scende a circa un quarto. Un rapporto per fascia farebbe
/// salti ai confini delle fasce, la curva no.
///
/// Sotto il primo punto vale il pavimento; oltre l'ultimo si applica il rapporto dell'ultimo punto.
/// </summary>
public sealed class SecretLairPremiumCurve
{
    /// <summary>Sotto questo numero di carte per fascia la mediana è troppo instabile.</summary>
    public const int MinSamplesPerPoint = 20;

    public IReadOnlyList<SecretLairCurvePoint> Points { get; }

    /// <summary>Prezzo mediano delle carte Secret Lair senza altre stampe: la stima, debole, per le carte nuove.</summary>
    public decimal NoBasePrice { get; }

    public int Samples { get; }
    public int NoBaseSamples { get; }

    private SecretLairPremiumCurve(IReadOnlyList<SecretLairCurvePoint> points, decimal noBasePrice, int samples, int noBaseSamples)
    {
        Points = points;
        NoBasePrice = noBasePrice;
        Samples = samples;
        NoBaseSamples = noBaseSamples;
    }

    /// <param name="samples">Coppie (prezzo della stampa base, prezzo della versione Secret Lair), entrambi positivi.</param>
    /// <param name="noBasePrices">Prezzi delle carte Secret Lair che non hanno altre stampe.</param>
    /// <param name="maxPoints">Numero massimo di fasce.</param>
    public static SecretLairPremiumCurve Fit(
        IEnumerable<(decimal BasePrice, decimal SecretLairPrice)> samples,
        IEnumerable<decimal> noBasePrices,
        int maxPoints = 10)
    {
        var sorted = samples.Where(s => s.BasePrice > 0 && s.SecretLairPrice > 0).OrderBy(s => s.BasePrice).ToList();
        var noBase = noBasePrices.Where(p => p > 0).ToList();

        var pointCount = Math.Min(maxPoints, sorted.Count / MinSamplesPerPoint);
        var points = new List<SecretLairCurvePoint>();
        if (pointCount == 0 && sorted.Count > 0) pointCount = 1;

        for (var i = 0; i < pointCount; i++)
        {
            var from = sorted.Count * i / pointCount;
            var to = sorted.Count * (i + 1) / pointCount;
            var slice = sorted.GetRange(from, to - from);
            points.Add(new SecretLairCurvePoint(
                Median(slice.Select(s => s.BasePrice)),
                Median(slice.Select(s => s.SecretLairPrice)),
                slice.Count));
        }

        // Una carta più cara non può valere meno in versione SL: le mediane di due fasce vicine a
        // volte si invertono per il rumore, e la curva resta non decrescente.
        for (var i = 1; i < points.Count; i++)
        {
            if (points[i].SecretLairPrice < points[i - 1].SecretLairPrice)
                points[i] = points[i] with { SecretLairPrice = points[i - 1].SecretLairPrice };
        }

        return new SecretLairPremiumCurve(points, noBase.Count > 0 ? Median(noBase) : 0m, sorted.Count, noBase.Count);
    }

    /// <summary>Prezzo stimato della versione Secret Lair; null se la curva non ha dati.</summary>
    public decimal? Estimate(decimal basePrice)
    {
        if (Points.Count == 0) return null;
        var first = Points[0];
        if (basePrice <= first.BasePrice) return first.SecretLairPrice;

        for (var i = 1; i < Points.Count; i++)
        {
            var (a, b) = (Points[i - 1], Points[i]);
            if (basePrice > b.BasePrice) continue;
            if (b.BasePrice == a.BasePrice) return b.SecretLairPrice;
            var t = (basePrice - a.BasePrice) / (b.BasePrice - a.BasePrice);
            return Math.Round(a.SecretLairPrice + t * (b.SecretLairPrice - a.SecretLairPrice), 2);
        }

        var last = Points[^1];
        return Math.Round(basePrice * last.SecretLairPrice / last.BasePrice, 2);
    }

    private static decimal Median(IEnumerable<decimal> values)
    {
        var list = values.OrderBy(v => v).ToList();
        if (list.Count == 0) return 0m;
        var mid = list.Count / 2;
        return list.Count % 2 == 1 ? list[mid] : Math.Round((list[mid - 1] + list[mid]) / 2, 2);
    }
}

/// <param name="BasePrice">Mediana del prezzo della stampa più economica nella fascia.</param>
/// <param name="SecretLairPrice">Mediana del prezzo della versione Secret Lair nella fascia.</param>
/// <param name="Samples">Carte della fascia.</param>
public record SecretLairCurvePoint(decimal BasePrice, decimal SecretLairPrice, int Samples);
