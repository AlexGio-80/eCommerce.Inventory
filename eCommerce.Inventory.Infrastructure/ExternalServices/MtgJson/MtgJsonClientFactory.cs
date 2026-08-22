using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.MtgJson;

/// <summary>
/// Factory for creating MtgJsonClient instances with proper HttpClient configuration
/// </summary>
public static class MtgJsonClientFactory
{
    public static MtgJsonClient Create(ILogger<MtgJsonClient> logger)
    {
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://mtgjson.com/api/v5/"),
            Timeout = TimeSpan.FromMinutes(5)
        };
        httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
        httpClient.DefaultRequestHeaders.Add("User-Agent", "eCommerceInventory/1.0");

        return new MtgJsonClient(httpClient, logger);
    }
}
