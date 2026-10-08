using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.SecretLair;

/// <summary>
/// Negozio Secret Lair di Wizards. Il catalogo la pagina lo disegna nel browser leggendolo
/// dall'interfaccia di ricerca della piattaforma Scalefast (<c>StoreSearch</c>): non è documentata e
/// può cambiare senza preavviso (scelta dell'utente, 08/10/2026). L'elenco delle carte di un prodotto
/// invece è nell'HTML della sua pagina.
/// </summary>
public interface ISecretLairShopClient
{
    Task<List<SecretLairShopItem>> GetCatalogAsync(CancellationToken cancellationToken = default);
    Task<List<SecretLairShopCardLine>> GetContentsAsync(string productId, CancellationToken cancellationToken = default);
}

public class SecretLairShopClient : ISecretLairShopClient
{
    private const int PageSize = 50;

    /// <summary>Oltre queste pagine c'è qualcosa che non va: il catalogo ha qualche centinaio di prodotti.</summary>
    private const int MaxPages = 20;

    private readonly HttpClient _httpClient;
    private readonly string _catalogUrl;
    private readonly string _productUrl;

    public SecretLairShopClient(HttpClient httpClient, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _httpClient = httpClient;
        _catalogUrl = configuration["SecretLair:Monitor:CatalogUrl"]
                      ?? "https://storesearch.eu.scalefast.com/StoreSearch?userID=10751401&locale=en_REU&currency=EUR&crit=ALL&sort=availability_wotc,featured&env=prod&filters=feedID:72135&offset={0}&count={1}";
        _productUrl = configuration["SecretLair:Monitor:ProductUrl"] ?? "https://secretlair.wizards.com/eu/product/{0}";
    }

    public async Task<List<SecretLairShopItem>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<SecretLairShopItem>();
        for (var page = 0; page < MaxPages; page++)
        {
            var url = string.Format(CultureInfo.InvariantCulture, _catalogUrl, page * PageSize, PageSize);
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var (pageItems, total) = ParseCatalog(await response.Content.ReadAsStringAsync(cancellationToken));
            items.AddRange(pageItems);

            if (pageItems.Count == 0 || items.Count >= total) break;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return items.GroupBy(i => i.ProductId).Select(g => g.First()).ToList();
    }

    public async Task<List<SecretLairShopCardLine>> GetContentsAsync(string productId, CancellationToken cancellationToken = default)
    {
        var html = await _httpClient.GetStringAsync(string.Format(CultureInfo.InvariantCulture, _productUrl, productId), cancellationToken);
        return ParseContents(html);
    }

    /// <summary>Una pagina del catalogo: i prodotti e il totale dichiarato.</summary>
    /// <exception cref="FormatException">La risposta non ha la forma attesa: l'interfaccia è cambiata.</exception>
    public static (List<SecretLairShopItem> Items, int Total) ParseCatalog(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("filters", out var filters) || filters.ValueKind != JsonValueKind.Array
            || filters.GetArrayLength() == 0 || !filters[0].TryGetProperty("products", out var products))
            throw new FormatException("Risposta del catalogo Secret Lair senza 'filters[0].products': l'interfaccia è cambiata");

        var total = Int(filters[0], "total") ?? 0;
        var items = new List<SecretLairShopItem>();

        foreach (var p in products.EnumerateArray())
        {
            var id = Str(p, "productID");
            if (string.IsNullOrEmpty(id)) continue;

            var title = p.TryGetProperty("descriptions", out var descriptions) && descriptions.ValueKind == JsonValueKind.Array
                ? descriptions.EnumerateArray().Where(d => Str(d, "lang") == "EN").Select(d => Str(d, "title")).FirstOrDefault()
                  ?? descriptions.EnumerateArray().Select(d => Str(d, "title")).FirstOrDefault()
                : null;

            var edition = p.TryGetProperty("specific", out var specific) ? Path(specific, "extension", "videogame", "edition") : null;
            var category = p.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array && categories.GetArrayLength() > 0
                ? Str(categories[0], "categoryName")
                : null;

            items.Add(new SecretLairShopItem(
                id,
                p.TryGetProperty("refs", out var refs) ? Str(refs, "refID") : null,
                title ?? id,
                CleanDropName(category),
                edition != null ? edition.Equals("FOIL", StringComparison.OrdinalIgnoreCase)
                                : (title ?? "").Contains("Foil", StringComparison.OrdinalIgnoreCase),
                Price(p),
                p.TryGetProperty("stock", out var stock) ? Int(stock, "stock") : null,
                Str(p, "preorder") == "1",
                Int(p, "limit_purchase") is { } limit && limit > 0 ? limit : null,
                DateTimeOffset.TryParse(Str(p, "release_date"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var release) ? release : null,
                p.TryGetProperty("flash_sales", out var sales) ? SaleDate(Str(sales, "start_date")) : null,
                p.TryGetProperty("flash_sales", out var sales2) ? SaleDate(Str(sales2, "end_date")) : null));
        }

        return (items, total);
    }

    private static readonly Regex CardLine = new(
        @"^\s*(?:(?<qty>\d+)\s*x\s+)?(?<name>.+?)(?:\s+as\s+[""“](?<alias>.+?)[""”])?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Elenco "Contents" della pagina del prodotto: una riga per carta, "1x Winota, Joiner of Forces
    /// as “He-Man, Champion of Eternia”". Vuoto se la pagina non lo riporta.
    /// </summary>
    public static List<SecretLairShopCardLine> ParseContents(string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        var items = document.QuerySelectorAll("#collapseLong li");
        if (items.Length == 0) items = document.QuerySelectorAll(".product-information li");

        return items
            .Select(li => CardLine.Match(System.Net.WebUtility.HtmlDecode(li.TextContent).Trim()))
            .Where(m => m.Success && m.Groups["name"].Value.Length > 0)
            .Select(m => new SecretLairShopCardLine(
                int.TryParse(m.Groups["qty"].Value, out var qty) ? qty : 1,
                m.Groups["name"].Value.Trim(),
                m.Groups["alias"].Success ? m.Groups["alias"].Value.Trim() : null))
            .ToList();
    }

    /// <summary>"bu-SL. Chaos Vault: The Oddlands" → "Chaos Vault: The Oddlands".</summary>
    private static string? CleanDropName(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return null;
        var dot = category.IndexOf(". ", StringComparison.Ordinal);
        return (dot >= 0 && dot < 12 ? category[(dot + 2)..] : category).Trim();
    }

    private static decimal Price(JsonElement p)
    {
        if (p.TryGetProperty("prices", out var prices) && prices.ValueKind == JsonValueKind.Array && prices.GetArrayLength() > 0
            && Dec(prices[0], "price") is { } price)
            return price;
        if (p.TryGetProperty("price_info", out var info) && info.TryGetProperty("prices", out var infoPrices)
            && infoPrices.ValueKind == JsonValueKind.Array && infoPrices.GetArrayLength() > 0 && Dec(infoPrices[0], "price") is { } infoPrice)
            return infoPrice;
        return 0m;
    }

    /// <summary>"2026-09-28 18:00:00"; le date lontanissime (2050) vogliono dire "nessuna fine".</summary>
    private static DateTime? SaleDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 2000 && date.Year < 2040
            ? date
            : null;

    private static string? Path(JsonElement element, params string[] path)
    {
        foreach (var key in path)
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(key, out element)) return null;
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
    }

    private static string? Str(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => "1",
                JsonValueKind.False => "0",
                _ => null
            }
            : null;

    private static int? Int(JsonElement element, string name) =>
        int.TryParse(Str(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static decimal? Dec(JsonElement element, string name) =>
        decimal.TryParse(Str(element, name), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;
}

public record SecretLairShopItem(
    string ProductId,
    string? RefId,
    string Title,
    string? DropName,
    bool IsFoil,
    decimal Price,
    int? Stock,
    bool IsPreorder,
    int? LimitPerCustomer,
    DateTimeOffset? ReleaseDate,
    DateTime? SaleStart,
    DateTime? SaleEnd);

public record SecretLairShopCardLine(int Quantity, string CardName, string? DisplayName);
