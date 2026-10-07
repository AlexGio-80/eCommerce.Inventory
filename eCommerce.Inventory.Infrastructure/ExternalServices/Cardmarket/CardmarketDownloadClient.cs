using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.Cardmarket;

/// <summary>
/// File pubblici di Cardmarket: catalogo prodotti e listino prezzi, rigenerati ogni giorno e
/// scaricabili senza login. L'API di Cardmarket non accetta nuove richieste di accesso, ma per
/// leggere i prezzi questi file bastano e contengono anche i sigillati in preordine.
/// </summary>
public interface ICardmarketDownloadClient
{
    /// <summary>
    /// <c>Last-Modified</c> del listino, con una richiesta HEAD: permette di non riscaricare 26 MB
    /// quando il file è lo stesso dell'ultimo import.
    /// </summary>
    Task<DateTimeOffset?> GetPriceGuideLastModifiedAsync(CancellationToken cancellationToken = default);

    Task<CardmarketPriceGuideFile> GetPriceGuideAsync(CancellationToken cancellationToken = default);
    Task<CardmarketProductListFile> GetSealedProductsAsync(CancellationToken cancellationToken = default);
    Task<CardmarketProductListFile> GetSingleProductsAsync(CancellationToken cancellationToken = default);
}

public class CardmarketDownloadClient : ICardmarketDownloadClient
{
    /// <summary>Il suffisso dei file è il gioco: 1 è Magic.</summary>
    private const int MagicGameId = 1;

    private readonly HttpClient _httpClient;

    public CardmarketDownloadClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private static string PriceGuidePath => $"priceGuide/price_guide_{MagicGameId}.json";

    public async Task<DateTimeOffset?> GetPriceGuideLastModifiedAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Head, PriceGuidePath);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return response.Content.Headers.LastModified;
    }

    public Task<CardmarketPriceGuideFile> GetPriceGuideAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<CardmarketPriceGuideFile>(PriceGuidePath, cancellationToken);

    public Task<CardmarketProductListFile> GetSealedProductsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<CardmarketProductListFile>($"productList/products_nonsingles_{MagicGameId}.json", cancellationToken);

    public Task<CardmarketProductListFile> GetSingleProductsAsync(CancellationToken cancellationToken = default) =>
        GetJsonAsync<CardmarketProductListFile>($"productList/products_singles_{MagicGameId}.json", cancellationToken);

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        // Lettura in streaming: i file arrivano a decine di MB, inutile tenerli anche come stringa.
        using var response = await _httpClient.GetAsync(path, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: cancellationToken)
               ?? throw new InvalidDataException($"File Cardmarket vuoto: {path}");
    }
}

public class CardmarketPriceGuideFile
{
    /// <summary>
    /// Formato "2026-10-07T02:49:35+0200": il fuso senza i due punti non è ISO 8601 esteso, e il
    /// deserializzatore lo rifiuterebbe. Resta stringa e si converte con <see cref="GetCreatedAt"/>.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = string.Empty;

    public DateTimeOffset GetCreatedAt() =>
        DateTimeOffset.Parse(CreatedAt, CultureInfo.InvariantCulture, DateTimeStyles.None);

    [JsonPropertyName("priceGuides")]
    public List<CardmarketPriceGuideEntry> PriceGuides { get; set; } = new();
}

public class CardmarketPriceGuideEntry
{
    [JsonPropertyName("idProduct")] public int IdProduct { get; set; }
    [JsonPropertyName("idCategory")] public int IdCategory { get; set; }
    [JsonPropertyName("avg")] public decimal? Avg { get; set; }
    [JsonPropertyName("low")] public decimal? Low { get; set; }
    [JsonPropertyName("trend")] public decimal? Trend { get; set; }
    [JsonPropertyName("avg1")] public decimal? Avg1 { get; set; }
    [JsonPropertyName("avg7")] public decimal? Avg7 { get; set; }
    [JsonPropertyName("avg30")] public decimal? Avg30 { get; set; }
    [JsonPropertyName("avg-foil")] public decimal? AvgFoil { get; set; }
    [JsonPropertyName("low-foil")] public decimal? LowFoil { get; set; }
    [JsonPropertyName("trend-foil")] public decimal? TrendFoil { get; set; }
}

public class CardmarketProductListFile
{
    [JsonPropertyName("products")]
    public List<CardmarketCatalogEntry> Products { get; set; } = new();
}

public class CardmarketCatalogEntry
{
    [JsonPropertyName("idProduct")] public int IdProduct { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("idCategory")] public int IdCategory { get; set; }
    [JsonPropertyName("categoryName")] public string CategoryName { get; set; } = string.Empty;
    [JsonPropertyName("idExpansion")] public int IdExpansion { get; set; }

    /// <summary>Formato "yyyy-MM-dd HH:mm:ss", non ISO: per questo resta stringa e si converte dopo.</summary>
    [JsonPropertyName("dateAdded")] public string? DateAdded { get; set; }
}
