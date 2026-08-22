using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.MtgJson;

/// <summary>
/// Client for MTGJSON API
/// Provides access to Italian card names from the AllPrintings.json data
/// </summary>
public class MtgJsonClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<MtgJsonClient> _logger;

    public MtgJsonClient(HttpClient httpClient, ILogger<MtgJsonClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Downloads and parses AllPrintings.json from MTGJSON
    /// Returns a dictionary mapping Scryfall Card ID -> Italian name
    /// </summary>
    public async Task<Dictionary<string, string>> GetItalianNamesByScryfallCardIdAsync(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, string>();

        try
        {
            _logger.LogInformation("Downloading MTGJSON AllPrintings.json...");
            var response = await _httpClient.GetAsync("AllPrintings.json", cancellationToken);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var dataElement))
            {
                _logger.LogWarning("MTGJSON response missing 'data' property");
                return result;
            }

            var count = 0;

            foreach (var setProperty in dataElement.EnumerateObject())
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var setCode = setProperty.Name;
                var setData = setProperty.Value;

                if (!setData.TryGetProperty("cards", out var cardsElement))
                    continue;

                foreach (var card in cardsElement.EnumerateArray())
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Get Scryfall Card ID (specific to this printing)
                    if (!card.TryGetProperty("identifiers", out var identifiers))
                        continue;

                    if (!identifiers.TryGetProperty("scryfallId", out var scryfallIdElement))
                        continue;

                    var scryfallCardId = scryfallIdElement.GetString();
                    if (string.IsNullOrWhiteSpace(scryfallCardId))
                        continue;

                    // Get foreignData array
                    if (!card.TryGetProperty("foreignData", out var foreignData))
                        continue;

                    foreach (var foreignEntry in foreignData.EnumerateArray())
                    {
                        if (!foreignEntry.TryGetProperty("language", out var langElement))
                            continue;

                        var language = langElement.GetString();
                        if (language != "Italian")
                            continue;

                        if (!foreignEntry.TryGetProperty("name", out var nameElement))
                            continue;

                        var italianName = nameElement.GetString();
                        if (string.IsNullOrWhiteSpace(italianName))
                            continue;

                        result[scryfallCardId] = italianName;
                        count++;
                        break;
                    }
                }
            }

            _logger.LogInformation("Parsed {Count} Italian card names from MTGJSON AllPrintings", count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading/parsing MTGJSON AllPrintings.json");
        }

        return result;
    }
}