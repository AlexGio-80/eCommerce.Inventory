using System.Globalization;
using System.Text.RegularExpressions;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Legge il testo di una pagina delle offerte di un venditore Cardmarket copiata dall'utente
/// (<c>.../Users/{venditore}/Offers/{categoria}</c>, Ctrl+A e Ctrl+C). Feature 007, Fase 1.
///
/// Ogni offerta arriva su righe separate: nome del prodotto, commento del venditore (facoltativo), prezzo
/// ("37,98 €"), quantità ("13"). La tabella comincia dopo l'intestazione "Name / Info. / Offer" e finisce alla
/// riga "N Hits". Il venditore e la categoria si leggono dal percorso in alto (User search › venditore ›
/// Offers › categoria). Regge sia il testo semplice sia quello con i link in formato Markdown.
///
/// La lingua è una bandierina, che nel testo non c'è: per questo si copia con il filtro Language: English, e
/// <see cref="CardmarketSellerPage.EnglishFilter"/> dice se il testo lo mostra.
/// </summary>
public static partial class CardmarketSellerPageParser
{
    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"^(?:[*\-•]\s+|\d+\.\s+)")]
    private static partial Regex ListMarker();

    [GeneratedRegex(@"^(\d{1,3}(?:\.\d{3})+|\d+),(\d{2})\s*€$")]
    private static partial Regex Price();

    [GeneratedRegex(@"^\d+$")]
    private static partial Regex Quantity();

    [GeneratedRegex(@"^\d+\s+Hits$", RegexOptions.IgnoreCase)]
    private static partial Regex Hits();

    [GeneratedRegex(@"English\s*\(\d+\)")]
    private static partial Regex EnglishFilterCount();

    public static CardmarketSellerPage Parse(string text)
    {
        var lines = (text ?? string.Empty)
            .Replace("\r", string.Empty)
            .Split('\n')
            // Il testo semplice di alcuni browser mette le celle di una riga separate da tabulazioni.
            .SelectMany(l => l.Split('\t'))
            .Select(Clean)
            .ToList();

        var (seller, category) = Breadcrumb(lines);
        var offers = new List<CardmarketPageOffer>();

        // La tabella comincia all'intestazione "Name", "Info.", "Offer" che segue il conteggio "N Hits".
        var start = -1;
        for (var i = 0; i + 2 < lines.Count; i++)
        {
            if (lines[i] == "Name" && lines[i + 1] == "Info." && lines[i + 2] == "Offer")
            {
                start = i + 3;
                break;
            }
        }

        if (start >= 0)
        {
            var block = new List<string>();
            for (var i = start; i < lines.Count; i++)
            {
                var line = lines[i];
                if (Hits().IsMatch(line)) break;
                if (line.Length == 0) continue;

                var price = ParsePrice(line);
                if (price != null && i + 1 < lines.Count && Quantity().IsMatch(lines[i + 1]) && block.Count > 0)
                {
                    var comment = block.Count > 1 ? string.Join(" ", block.Skip(1)) : null;
                    offers.Add(new CardmarketPageOffer(block[0], price.Value, int.Parse(lines[i + 1], CultureInfo.InvariantCulture),
                        comment, IsDamaged(comment)));
                    block.Clear();
                    i++;
                    continue;
                }
                block.Add(line);
            }
        }

        return new CardmarketSellerPage(seller, category, EnglishFilterCount().IsMatch(text ?? string.Empty), offers);
    }

    /// <summary>Nome del catalogo e nome della pagina a confronto: virgolette raddoppiate dall'import, curve, spazi.</summary>
    public static string NormalizeName(string name) =>
        Regex.Replace(
            name.Replace("\"\"", "\"").Replace('“', '"').Replace('”', '"').Replace('’', '\'').Trim(),
            @"\s+", " ")
        .ToLowerInvariant();

    private static string Clean(string line)
    {
        var l = MarkdownLink().Replace(line.Trim(), "$1").Trim();
        return ListMarker().Replace(l, string.Empty).Trim();
    }

    private static decimal? ParsePrice(string line)
    {
        var m = Price().Match(line);
        if (!m.Success) return null;
        var units = m.Groups[1].Value.Replace(".", string.Empty);
        return decimal.Parse($"{units}.{m.Groups[2].Value}", CultureInfo.InvariantCulture);
    }

    /// <summary>Venditore e categoria dal percorso "User search › venditore › Offers › categoria".</summary>
    private static (string? Seller, string? Category) Breadcrumb(List<string> lines)
    {
        for (var i = 1; i + 1 < lines.Count; i++)
        {
            if (lines[i] == "Offers" && lines[i - 1].Length > 0 && i >= 2 && lines[i - 2] == "User search")
                return (lines[i - 1], lines[i + 1].Length > 0 ? lines[i + 1] : null);
        }
        return (null, null);
    }

    private static bool IsDamaged(string? comment) =>
        comment != null && (comment.Contains("damaged", StringComparison.OrdinalIgnoreCase)
                            || comment.Contains("danneggiat", StringComparison.OrdinalIgnoreCase)
                            || comment.Contains("beschädigt", StringComparison.OrdinalIgnoreCase));
}

/// <param name="EnglishFilter">Il testo mostra il filtro "English (N)": le offerte sono in inglese.</param>
public record CardmarketSellerPage(string? Seller, string? Category, bool EnglishFilter, List<CardmarketPageOffer> Offers);

public record CardmarketPageOffer(string Name, decimal Price, int Quantity, string? Comment, bool IsDamaged);
