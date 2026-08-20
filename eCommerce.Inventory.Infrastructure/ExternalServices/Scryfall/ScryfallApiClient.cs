using System.Net.Http.Json;
using eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall.DTOs;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.Scryfall;

public class ScryfallApiClient : IScryfallApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ScryfallApiClient> _logger;

    public ScryfallApiClient(HttpClient httpClient, ILogger<ScryfallApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<ScryfallSetDto>> GetSetsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching sets from Scryfall API");
            var response = await _httpClient.GetAsync("sets", cancellationToken);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ScryfallSetsResponse>(cancellationToken: cancellationToken);
            return result?.Data ?? new List<ScryfallSetDto>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching sets from Scryfall API");
            return new List<ScryfallSetDto>();
        }
    }

    public async Task<ScryfallCardDto?> GetCardByIdAsync(string scryfallId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scryfallId))
        {
            return null;
        }

        try
        {
            _logger.LogDebug("Fetching card {ScryfallId} from Scryfall API", scryfallId);

            // Scryfall rate limit: 10 requests/second, be polite
            await Task.Delay(100, cancellationToken);

            var response = await _httpClient.GetAsync($"cards/{scryfallId}", cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                _logger.LogWarning("Card {ScryfallId} not found on Scryfall (404)", scryfallId);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var card = await response.Content.ReadFromJsonAsync<ScryfallCardDto>(cancellationToken: cancellationToken);
            return card;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching card {ScryfallId} from Scryfall API", scryfallId);
            return null;
        }
    }
}
