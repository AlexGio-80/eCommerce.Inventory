using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Legge una carta Magic dalla foto con Claude (API Anthropic). Una sola chiamata, risposta in JSON
/// vincolato a uno schema. La chiave sta in <c>Anthropic:ApiKey</c>, solo nella configurazione di
/// produzione; il modello in <c>Scan:Model</c> (predefinito Claude Opus 5.5).
///
/// Il lettore non sceglie la carta: restituisce quello che è stampato, e la corrispondenza con i
/// blueprint la fa <see cref="CardScanService"/>.
/// </summary>
public class ClaudeCardImageReader : ICardImageReader
{
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>Le lingue con i nomi usati dalla maschera "Nuovo Prodotto" e da Card Trader.</summary>
    public static readonly string[] Languages =
        { "English", "Italian", "Japanese", "French", "German", "Spanish", "Chinese", "Portuguese", "Russian", "Korean" };

    private const string Instructions = """
        You read Magic: The Gathering cards from phone photos so a seller can find the exact printing.
        Report only what you can see. Use null for anything you cannot read.
        - name: the card's English name. If the card is printed in another language and you know the English name, give it; otherwise repeat the printed name.
        - printedName: the name exactly as printed.
        - setCode: the set code printed in the bottom-left info line on modern cards (e.g. "2X2", "MKM"), lowercase. If there is no printed code, give your best identification from the set symbol only if you are confident, otherwise null.
        - collectorNumber: the collector number before the slash, as printed (e.g. "117" from "117/332"). Keep letters such as "a" or "★".
        - language: the language the card is printed in.
        - isFoil: true only if a foil sheen is clearly visible.
        - confidence: "high" if name, set and number are all clearly readable, "medium" if one of them is uncertain, "low" otherwise.
        - notes: one short sentence in Italian about anything unreadable or doubtful, or null.
        """;

    private readonly IConfiguration _configuration;
    private readonly ILogger<ClaudeCardImageReader> _logger;
    private AnthropicClient? _client;

    public ClaudeCardImageReader(IConfiguration configuration, ILogger<ClaudeCardImageReader> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    private string? ApiKey => _configuration["Anthropic:ApiKey"];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<CardReading> ReadAsync(byte[] image, string mediaType, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("Chiave API Anthropic non configurata (Anthropic:ApiKey)");

        _client ??= new AnthropicClient { ApiKey = ApiKey };
        var model = _configuration["Scan:Model"] ?? DefaultModel;

        var response = await _client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 4096,
            System = Instructions,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.Low,
                Format = new JsonOutputFormat { Schema = Schema },
            },
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = new List<ContentBlockParam>
                    {
                        new ImageBlockParam
                        {
                            Source = new Base64ImageSource
                            {
                                Data = Convert.ToBase64String(image),
                                MediaType = mediaType switch
                                {
                                    "image/png" => MediaType.ImagePng,
                                    "image/webp" => MediaType.ImageWebP,
                                    _ => MediaType.ImageJpeg,
                                },
                            },
                        },
                        new TextBlockParam { Text = "Read this card." },
                    },
                },
            ],
        }, cancellationToken);

        _logger.LogInformation(
            "Riconoscimento carta con {Model}: {InputTokens} token in ingresso, {OutputTokens} in uscita, esito {StopReason}",
            model, response.Usage.InputTokens, response.Usage.OutputTokens, response.StopReason);

        if (response.StopReason == StopReason.Refusal)
            throw new InvalidOperationException("Claude ha rifiutato di leggere la foto");

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        return Parse(json);
    }

    /// <summary>Lettura dalla risposta JSON, con lingua e set riportati nella forma del database.</summary>
    public static CardReading Parse(string json)
    {
        var reading = JsonSerializer.Deserialize<CardReading>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                      ?? new CardReading();

        reading.Name = Clean(reading.Name);
        reading.PrintedName = Clean(reading.PrintedName);
        reading.SetCode = Clean(reading.SetCode)?.ToLowerInvariant();
        reading.CollectorNumber = Clean(reading.CollectorNumber);
        reading.Language = NormalizeLanguage(reading.Language);
        reading.Notes = Clean(reading.Notes);
        return reading;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language)) return null;
        var l = language.Trim();
        var known = Languages.FirstOrDefault(x => x.Equals(l, StringComparison.OrdinalIgnoreCase));
        if (known != null) return known;

        return l.ToLowerInvariant() switch
        {
            "en" or "inglese" => "English",
            "it" or "italiano" => "Italian",
            "ja" or "jp" or "giapponese" => "Japanese",
            "fr" or "francese" => "French",
            "de" or "tedesco" => "German",
            "es" or "spagnolo" => "Spanish",
            "zh" or "cinese" or "chinese simplified" or "chinese traditional" => "Chinese",
            "pt" or "portoghese" => "Portuguese",
            "ru" or "russo" => "Russian",
            "ko" or "coreano" => "Korean",
            _ => l
        };
    }

    private static readonly Dictionary<string, JsonElement> Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
        {
          "type": "object",
          "properties": {
            "name": { "type": ["string", "null"] },
            "printedName": { "type": ["string", "null"] },
            "setCode": { "type": ["string", "null"] },
            "collectorNumber": { "type": ["string", "null"] },
            "language": { "type": ["string", "null"] },
            "isFoil": { "type": "boolean" },
            "confidence": { "type": "string", "enum": ["high", "medium", "low"] },
            "notes": { "type": ["string", "null"] }
          },
          "required": ["name", "printedName", "setCode", "collectorNumber", "language", "isFoil", "confidence", "notes"],
          "additionalProperties": false
        }
        """)!;
}
