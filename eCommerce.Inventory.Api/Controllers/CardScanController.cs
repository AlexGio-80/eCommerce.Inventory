using eCommerce.Inventory.Api.Models;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using eCommerce.Inventory.Infrastructure.Services;
using Microsoft.AspNetCore.Mvc;

namespace eCommerce.Inventory.Api.Controllers;

/// <summary>
/// Sezione per il telefono (feature 006, Fase 1): riconoscimento di una carta dalla foto e scheda di
/// valutazione. Protetto come tutto il resto dal criterio globale (ruolo Admin).
/// </summary>
[ApiController]
[Route("api/scan")]
public class CardScanController : ControllerBase
{
    /// <summary>Il telefono riduce la foto prima di mandarla: oltre questa misura c'è qualcosa che non va.</summary>
    private const long MaxImageBytes = 8 * 1024 * 1024;

    private static readonly string[] AllowedMediaTypes = { "image/jpeg", "image/png", "image/webp" };

    private readonly CardScanService _scan;
    private readonly ILogger<CardScanController> _logger;

    public CardScanController(CardScanService scan, ILogger<CardScanController> logger)
    {
        _scan = scan;
        _logger = logger;
    }

    /// <summary>Legge la carta dalla foto e restituisce le carte del database che le corrispondono.</summary>
    [HttpPost("recognize")]
    [RequestSizeLimit(MaxImageBytes + 64 * 1024)]
    public async Task<IActionResult> Recognize(IFormFile image, CancellationToken cancellationToken)
    {
        if (!_scan.IsConfigured)
            return StatusCode(503, ApiResponse<object>.ErrorResult(
                "Il riconoscimento non è configurato: manca la chiave API Anthropic sul PC"));

        if (image == null || image.Length == 0)
            return BadRequest(ApiResponse<object>.ErrorResult("Nessuna foto ricevuta"));
        if (image.Length > MaxImageBytes)
            return BadRequest(ApiResponse<object>.ErrorResult("Foto troppo grande"));

        var mediaType = image.ContentType?.ToLowerInvariant() ?? string.Empty;
        if (!AllowedMediaTypes.Contains(mediaType))
            return BadRequest(ApiResponse<object>.ErrorResult($"Formato non supportato: {mediaType}"));

        using var buffer = new MemoryStream();
        await image.CopyToAsync(buffer, cancellationToken);

        try
        {
            // Le foto non si salvano: servono solo per la risposta.
            var result = await _scan.RecognizeAsync(buffer.ToArray(), mediaType, cancellationToken);
            return Ok(ApiResponse<object>.SuccessResult(result));
        }
        catch (ScanLimitReachedException ex)
        {
            return StatusCode(429, ApiResponse<object>.ErrorResult(ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Riconoscimento della carta fallito");
            return StatusCode(502, ApiResponse<object>.ErrorResult("Riconoscimento non riuscito. Riprova con un'altra foto"));
        }
    }

    /// <summary>Scheda di valutazione di una carta per condizione, lingua e foil.</summary>
    [HttpGet("card/{blueprintId:int}")]
    public async Task<IActionResult> Valuate(
        int blueprintId,
        CancellationToken cancellationToken,
        [FromQuery] string condition = "Near Mint",
        [FromQuery] string language = "English",
        [FromQuery] bool isFoil = false)
    {
        // Come la maschera "Nuovo Prodotto": la lettura delle offerte passa davanti al lavoro in background.
        using var interactive = CardTraderRequestPriority.BeginInteractive();

        var valuation = await _scan.ValuateAsync(blueprintId, condition, language, isFoil, cancellationToken);
        if (valuation == null) return NotFound(ApiResponse<object>.ErrorResult($"Carta {blueprintId} non trovata"));

        return Ok(ApiResponse<object>.SuccessResult(valuation));
    }
}
