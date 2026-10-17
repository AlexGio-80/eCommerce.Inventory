using System.Text.Json;
using Anthropic.Models.Messages;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Stima della condizione di una carta dalle foto (fronte e, se c'è, retro) con Claude (feature 006,
/// Fase 2). È una proposta: la condizione su Card Trader la sceglie sempre l'utente.
///
/// Nel dubbio la stima sceglie la condizione peggiore: una Near Mint che arriva Slightly Played costa
/// un reclamo, una Slightly Played che arriva Near Mint solo qualche centesimo.
/// </summary>
public class ClaudeGradingService : IGradingService
{
    /// <summary>Le condizioni di Card Trader, dalla migliore alla peggiore, come nella maschera.</summary>
    public static readonly (string Code, string Name)[] Conditions =
    {
        ("NM", "Near Mint"), ("SP", "Slightly Played"), ("MP", "Moderately Played"), ("PL", "Played"), ("PO", "Poor")
    };

    private const string Instructions = """
        You estimate the condition of Magic: The Gathering cards from phone photos for a seller listing them on Card Trader.
        The photos show the same card. Each photo is introduced by a label: front, back, or detail. Detail photos
        are taken under raking light, against the light, or close up, to show wear that the plain photos hide:
        trust them over the plain photos. Judge only what is visible.

        Card Trader conditions:
        - NM (Near Mint): looks unplayed; at most a tiny white speck on an edge or corner.
        - SP (Slightly Played): light edge or corner whitening, a few light scratches or scuffs; no creases or bends.
        - MP (Moderately Played): clear whitening on several edges or corners, visible scratches or scuffs, minor surface wear; still no hard creases.
        - PL (Played): heavy wear, a crease or bend, clouding, or small writing/stains.
        - PO (Poor): tears, water damage, heavy creases, missing pieces.

        Printed design is never damage. The standard Magic card back is the same on every card: brown frame, the
        blue oval "Magic: The Gathering" logo, five colored orbs, swirling colors and gradients, and the "Deckmaster"
        lettering at the top, which has its own printed colored marks and strokes. None of these are ink, writing,
        stains or scratches. The same goes for the front: frame colors, foil patterns, holo stamps, artist signatures
        printed in the art, set symbols and textures belong to the card. Only physical wear counts: whitening, scratches,
        scuffs, dents, creases, bends, clouding, and writing or stains that are clearly not part of the print.
        If you are unsure whether a mark is printed or added, mention it in the note but do not grade on it.

        Wear that is easy to miss and does lower the grade:
        - small white nicks or specks along black front borders and on corners: even a few mean at least SP;
        - nicked, wavy or uneven edges, including a rough or irregular cut: at least SP, MP if clearly visible;
        - a back that looks dirty, hazy, yellowed or blotchy (aging, humidity, clouding), often visible only
          against the light: at least MP;
        - a slight bend or warp visible in raking light: at least MP.

        Rules:
        - When torn between two conditions, choose the worse one. Overgrading is the costly mistake.
        - The back usually shows edge wear more clearly: weigh it when present.
        - If the card is in a sleeve or the photo is blurry, too dark, glared or cropped, say so in summary, lower the confidence, and do not assume the hidden parts are perfect.
        - Scores go from 1 (destroyed) to 10 (pristine). overallGrade is the worst aspect, not the average.
        - Notes and summary are short sentences in Italian, naming where the wear is (e.g. "angolo in alto a destra sbiancato").
        """;

    private readonly AnthropicClientProvider _anthropic;
    private readonly ILogger<ClaudeGradingService> _logger;

    public ClaudeGradingService(AnthropicClientProvider anthropic, ILogger<ClaudeGradingService> logger)
    {
        _anthropic = anthropic;
        _logger = logger;
    }

    public Task<GradingResultDto> GradeCardAsync(Stream imageStream, string fileName)
        => GradeCardFromMultipleImagesAsync(new List<(Stream, string)> { (imageStream, fileName) });

    public async Task<GradingResultDto> GradeCardFromMultipleImagesAsync(List<(Stream ImageStream, string FileName)> images)
    {
        var client = _anthropic.Client;
        var model = _anthropic.GradingModel;

        var content = new List<ContentBlockParam>();
        var roles = PhotoRoles(images.Select(i => i.FileName).ToList());
        for (var i = 0; i < images.Count; i++)
        {
            using var buffer = new MemoryStream();
            await images[i].ImageStream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();
            content.Add(new TextBlockParam { Text = $"Photo {i + 1}: {roles[i]}" });
            content.Add(AnthropicClientProvider.ImageBlock(bytes, AnthropicClientProvider.DetectMediaType(bytes)));
        }
        content.Add(new TextBlockParam { Text = "Estimate the condition of this card." });

        var response = await client.Messages.Create(new MessageCreateParams
        {
            Model = model,
            MaxTokens = 8192,
            System = Instructions,
            OutputConfig = new OutputConfig
            {
                Effort = Effort.High,
                Format = new JsonOutputFormat { Schema = Schema },
            },
            Messages = [new() { Role = Role.User, Content = content }],
        });

        _logger.LogInformation(
            "Stima condizione con {Model} su {Images} foto: {InputTokens} token in ingresso, {OutputTokens} in uscita, esito {StopReason}",
            model, images.Count, response.Usage.InputTokens, response.Usage.OutputTokens, response.StopReason);

        if (response.StopReason == StopReason.Refusal)
            throw new InvalidOperationException("Claude ha rifiutato di valutare la foto");

        var json = string.Concat(response.Content.Select(b => b.Value).OfType<TextBlock>().Select(t => t.Text));
        var result = Parse(json);
        result.ImagesAnalyzed = images.Count;
        var proposedByModel = result.ConditionCode;
        ApplyNearMintGuard(result, hasBack: roles.Contains("back"));

        // Per le prove dell'utente: dal log si confrontano le proposte con la condizione vera.
        _logger.LogInformation(
            "Stima condizione: proposta {Condition} (Claude {ModelCondition}), sicurezza {Confidence}, foto {Roles}",
            result.ConditionCode, proposedByModel, result.Confidence, string.Join("+", roles));
        return result;
    }

    /// <summary>
    /// Ruolo di ogni foto dal nome del file che manda il telefono ("fronte", "retro", "dettaglio-N").
    /// Dalle pagine del PC i nomi sono qualsiasi: lì la prima è il fronte e la seconda il retro, come
    /// nella pagina "AI Grading"; le altre sono dettagli.
    /// </summary>
    public static List<string> PhotoRoles(IReadOnlyList<string> fileNames)
    {
        static string? Named(string name)
        {
            var n = name.ToLowerInvariant();
            if (n.StartsWith("fronte")) return "front";
            if (n.StartsWith("retro")) return "back";
            if (n.StartsWith("dettaglio")) return "detail";
            return null;
        }

        var named = fileNames.Select(Named).ToList();
        if (named.Any(r => r != null))
            return named.Select(r => r ?? "detail").ToList();

        return fileNames.Select((_, i) => i == 0 ? "front" : i == 1 ? "back" : "detail").ToList();
    }

    /// <summary>
    /// Near Mint solo con fronte e retro fotografati e sicurezza alta: è la proposta più costosa da sbagliare,
    /// e non la si lascia decidere all'IA da sola. Negli altri casi si propone Slightly Played e lo si dice.
    /// </summary>
    public static GradingResultDto ApplyNearMintGuard(GradingResultDto result, bool hasBack)
    {
        if (result.ConditionCode != "NM" || (hasBack && result.Confidence >= 0.85m)) return result;

        result.ConditionCode = "SP";
        result.ConditionName = "Slightly Played";
        result.NearMintPossible = true;
        var why = hasBack ? "la sicurezza della stima non è alta" : "manca la foto del retro";
        result.Summary = $"Potrebbe essere Near Mint, ma {why}: controlla la carta. {result.Summary}".Trim();
        return result;
    }

    /// <summary>Risposta JSON nel formato di <see cref="GradingResultDto"/>, condizione nella forma della maschera.</summary>
    public static GradingResultDto Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var code = root.GetProperty("condition").GetString()?.Trim().ToUpperInvariant() ?? "PO";
        var condition = Conditions.FirstOrDefault(c => c.Code == code);
        if (condition.Code == null) condition = Conditions[^1];

        var confidence = root.GetProperty("confidence").GetString() switch
        {
            "high" => 0.9m,
            "medium" => 0.7m,
            _ => 0.4m
        };

        return new GradingResultDto
        {
            ConditionCode = condition.Code,
            ConditionName = condition.Name,
            OverallGrade = Score(root, "overallGrade"),
            Centering = Score(root, "centering"),
            Corners = Score(root, "corners"),
            Edges = Score(root, "edges"),
            Surface = Score(root, "surface"),
            Confidence = confidence,
            Provider = "Claude",
            CenteringNote = Note(root, "centeringNote"),
            CornersNote = Note(root, "cornersNote"),
            EdgesNote = Note(root, "edgesNote"),
            SurfaceNote = Note(root, "surfaceNote"),
            Summary = Note(root, "summary")
        };
    }

    private static decimal Score(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.TryGetDecimal(out var d) ? Math.Clamp(d, 1m, 10m) : 1m;

    private static string? Note(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()!.Trim()
            : null;

    private static readonly Dictionary<string, JsonElement> Schema = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>("""
        {
          "type": "object",
          "properties": {
            "condition": { "type": "string", "enum": ["NM", "SP", "MP", "PL", "PO"] },
            "confidence": { "type": "string", "enum": ["high", "medium", "low"] },
            "overallGrade": { "type": "number" },
            "centering": { "type": "number" },
            "corners": { "type": "number" },
            "edges": { "type": "number" },
            "surface": { "type": "number" },
            "centeringNote": { "type": "string" },
            "cornersNote": { "type": "string" },
            "edgesNote": { "type": "string" },
            "surfaceNote": { "type": "string" },
            "summary": { "type": "string" }
          },
          "required": ["condition", "confidence", "overallGrade", "centering", "corners", "edges", "surface",
                       "centeringNote", "cornersNote", "edgesNote", "surfaceNote", "summary"],
          "additionalProperties": false
        }
        """)!;
}
