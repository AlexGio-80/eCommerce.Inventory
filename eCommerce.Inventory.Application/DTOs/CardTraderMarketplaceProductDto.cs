using System.Text.Json;
using System.Text.Json.Serialization;

namespace eCommerce.Inventory.Application.DTOs;

public class CardTraderMarketplaceProductDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("blueprint_id")]
    public int BlueprintId { get; set; }

    [JsonPropertyName("price_cents")]
    public int PriceCents { get; set; }

    [JsonPropertyName("price_currency")]
    public string PriceCurrency { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("properties_hash")]
    public Dictionary<string, object> PropertiesHash { get; set; } = new();

    private CardTraderMarketplacePropertiesDto? _properties;

    /// <summary>
    /// Condizione, lingua e finitura della carta.
    ///
    /// L'API v2 NON restituisce un oggetto "properties": i valori stanno dentro
    /// "properties_hash", con chiavi che dipendono dal gioco ("mtg_language",
    /// "pokemon_language", "mtg_foil", ...). Questa proprietà li estrae da lì.
    /// Il setter resta disponibile per costruire istanze nei test.
    /// </summary>
    [JsonIgnore]
    public CardTraderMarketplacePropertiesDto Properties
    {
        get => _properties ??= ParsePropertiesHash();
        set => _properties = value;
    }

    private CardTraderMarketplacePropertiesDto ParsePropertiesHash() => new()
    {
        Condition = ReadString("condition") ?? string.Empty,
        Language = ReadStringBySuffix("_language", "language") ?? string.Empty,
        IsFoil = ReadBoolBySuffix("_foil", "foil"),
        IsSigned = ReadBool("signed"),
        IsAltered = ReadBool("altered")
    };

    private string? ReadString(string key)
        => PropertiesHash.TryGetValue(key, out var v) ? AsString(v) : null;

    /// <summary>Cerca la chiave esatta e, in mancanza, la prima che termina con il suffisso indicato.</summary>
    private string? ReadStringBySuffix(string suffix, string exactKey)
    {
        if (PropertiesHash.TryGetValue(exactKey, out var exact)) return AsString(exact);

        foreach (var kv in PropertiesHash)
        {
            if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return AsString(kv.Value);
        }

        return null;
    }

    private bool ReadBool(string key)
        => PropertiesHash.TryGetValue(key, out var v) && AsBool(v);

    private bool ReadBoolBySuffix(string suffix, string exactKey)
    {
        if (PropertiesHash.TryGetValue(exactKey, out var exact)) return AsBool(exact);

        foreach (var kv in PropertiesHash)
        {
            if (kv.Key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return AsBool(kv.Value);
        }

        return false;
    }

    // I valori del dizionario arrivano come JsonElement quando la risposta è
    // deserializzata da System.Text.Json, ma possono essere tipi nativi se
    // l'oggetto è costruito a mano (test).
    private static string? AsString(object? value) => value switch
    {
        null => null,
        JsonElement e when e.ValueKind == JsonValueKind.String => e.GetString(),
        JsonElement e when e.ValueKind == JsonValueKind.Null => null,
        JsonElement e => e.ToString(),
        _ => value.ToString()
    };

    private static bool AsBool(object? value) => value switch
    {
        null => false,
        JsonElement e when e.ValueKind == JsonValueKind.True => true,
        JsonElement e when e.ValueKind == JsonValueKind.False => false,
        JsonElement e when e.ValueKind == JsonValueKind.String => bool.TryParse(e.GetString(), out var b) && b,
        bool b2 => b2,
        _ => bool.TryParse(value.ToString(), out var b3) && b3
    };

    /// <summary>Carta valutata da un ente di grading: non comparabile con una carta sciolta.</summary>
    [JsonPropertyName("graded")]
    public bool Graded { get; set; }

    /// <summary>Venditore in vacanza: l'offerta è visibile ma non acquistabile.</summary>
    [JsonPropertyName("on_vacation")]
    public bool OnVacation { get; set; }

    [JsonPropertyName("user")]
    public CardTraderMarketplaceUserDto User { get; set; } = new();
}

public class CardTraderMarketplacePropertiesDto
{
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = string.Empty;

    [JsonPropertyName("language")]
    public string Language { get; set; } = string.Empty;

    [JsonPropertyName("mtg_foil")]
    public bool IsFoil { get; set; }

    [JsonPropertyName("signed")]
    public bool IsSigned { get; set; }

    [JsonPropertyName("altered")]
    public bool IsAltered { get; set; }
}

/// <summary>
/// Venditore di un'offerta marketplace. Questi sono TUTTI i campi che l'API v2 restituisce
/// (verificato su un campione di offerte reali): in particolare non esiste alcun campo con
/// il numero di recensioni o il feedback, quindi un filtro "almeno N recensioni" non è
/// realizzabile. Al suo posto si usano UserType, CountryCode e MaxSellableIn24hQuantity,
/// affiancati dallo scarto statistico degli outlier nel PricingEngine.
/// </summary>
public class CardTraderMarketplaceUserDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>Valori osservati: "pro" e "normal".</summary>
    [JsonPropertyName("user_type")]
    public string UserType { get; set; } = string.Empty;

    /// <summary>Codice paese ISO a due lettere (es. "IT", "US").</summary>
    [JsonPropertyName("country_code")]
    public string CountryCode { get; set; } = string.Empty;

    /// <summary>
    /// Quantità massima vendibile in 24h: proxy della dimensione del venditore.
    /// Card Trader la restituisce null per una parte dei venditori, quindi è annullabile:
    /// "capacità sconosciuta" è un'informazione diversa da "capacità zero".
    /// </summary>
    [JsonPropertyName("max_sellable_in24h_quantity")]
    public int? MaxSellableIn24hQuantity { get; set; }

    [JsonPropertyName("one_day_ready")]
    public bool OneDayReady { get; set; }

    [JsonPropertyName("can_sell_via_hub")]
    public bool CanSellViaHub { get; set; }

    [JsonPropertyName("can_sell_sealed_with_ct_zero")]
    public bool CanSellSealedWithCtZero { get; set; }
}
