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
}
