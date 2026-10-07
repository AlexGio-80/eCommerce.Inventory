using System.Text.Json;
using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.MtgJson;

/// <summary>
/// <c>SetList.json</c> di MTGJSON: l'elenco delle espansioni con, per ciascuna, tutti i prodotti
/// sigillati e il loro contenuto. Circa 12 MB per l'intero catalogo, contro i centinaia di MB di
/// <c>AllPrintings.json</c>: per l'analisi acquisti basta questo.
/// </summary>
public interface IMtgJsonSetListClient
{
    Task<MtgJsonSetListFile> GetSetListAsync(CancellationToken cancellationToken = default);
}

public class MtgJsonSetListClient : IMtgJsonSetListClient
{
    private readonly HttpClient _httpClient;

    public MtgJsonSetListClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MtgJsonSetListFile> GetSetListAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("SetList.json", HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<MtgJsonSetListFile>(stream, cancellationToken: cancellationToken)
               ?? throw new InvalidDataException("SetList.json di MTGJSON vuoto");
    }
}

public class MtgJsonSetListFile
{
    [JsonPropertyName("data")] public List<MtgJsonSetDto> Data { get; set; } = new();
}

public class MtgJsonSetDto
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("parentCode")] public string? ParentCode { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("releaseDate")] public string? ReleaseDate { get; set; }
    [JsonPropertyName("sealedProduct")] public List<MtgJsonSealedProductDto>? SealedProduct { get; set; }
}

public class MtgJsonSealedProductDto
{
    [JsonPropertyName("uuid")] public Guid Uuid { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("subtype")] public string? Subtype { get; set; }
    [JsonPropertyName("cardCount")] public int? CardCount { get; set; }
    [JsonPropertyName("identifiers")] public Dictionary<string, string>? Identifiers { get; set; }
    [JsonPropertyName("contents")] public MtgJsonSealedContentsDto? Contents { get; set; }
}

public class MtgJsonSealedContentsDto
{
    [JsonPropertyName("pack")] public List<MtgJsonPackDto>? Pack { get; set; }
    [JsonPropertyName("sealed")] public List<MtgJsonSealedRefDto>? Sealed { get; set; }
    [JsonPropertyName("deck")] public List<MtgJsonNamedRefDto>? Deck { get; set; }
    [JsonPropertyName("card")] public List<MtgJsonCardRefDto>? Card { get; set; }
    [JsonPropertyName("other")] public List<MtgJsonNamedRefDto>? Other { get; set; }

    /// <summary>Configurazioni alternative: si registra solo che esistono, non si scompongono.</summary>
    [JsonPropertyName("variable")] public List<JsonElement>? Variable { get; set; }
}

public class MtgJsonPackDto
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
    [JsonPropertyName("set")] public string? Set { get; set; }
}

public class MtgJsonSealedRefDto
{
    [JsonPropertyName("count")] public int Count { get; set; } = 1;
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("set")] public string? Set { get; set; }
    [JsonPropertyName("uuid")] public Guid? Uuid { get; set; }
}

public class MtgJsonNamedRefDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("set")] public string? Set { get; set; }
}

public class MtgJsonCardRefDto
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("set")] public string? Set { get; set; }
    [JsonPropertyName("uuid")] public Guid? Uuid { get; set; }
    [JsonPropertyName("foil")] public bool? Foil { get; set; }
}
