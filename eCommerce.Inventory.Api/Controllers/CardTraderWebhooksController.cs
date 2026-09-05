using eCommerce.Inventory.Api.Filters;
using eCommerce.Inventory.Application.Commands;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace eCommerce.Inventory.Api.Controllers;

/// <summary>
/// Controller for handling Card Trader webhook events
/// Processes order creation, updates, and deletion notifications
/// </summary>
[ApiController]
[Route("api/[controller]")]
// Chiama Card Trader, che non ha un token dell'applicazione: l'autenticazione qui è la firma
// HMAC del payload, verificata a ogni richiesta da WebhookSignatureVerificationService.
[AllowAnonymous]
public class CardTraderWebhooksController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly WebhookSignatureVerificationService _signatureVerificationService;
    private readonly ILogger<CardTraderWebhooksController> _logger;

    public CardTraderWebhooksController(
        IMediator mediator,
        WebhookSignatureVerificationService signatureVerificationService,
        ILogger<CardTraderWebhooksController> logger)
    {
        _mediator = mediator;
        _signatureVerificationService = signatureVerificationService;
        _logger = logger;
    }

    /// <summary>
    /// Receive and process Card Trader webhook events
    /// Supports order.create, order.update, and order.destroy events
    /// </summary>
    /// <param name="webhook">The webhook payload from Card Trader</param>
    /// <returns>NoContent on success</returns>
    [HttpPost("events")]
    [EnableRequestBodyBuffering]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> HandleWebhookEvent([FromBody] WebhookDto webhook)
    {
        try
        {
            // Validate webhook payload
            if (webhook == null)
            {
                _logger.LogWarning("Received null webhook payload");
                return BadRequest("Webhook payload is required");
            }

            _logger.LogInformation(
                "Received Card Trader webhook - ID: {WebhookId}, Cause: {Cause}, ObjectId: {ObjectId}",
                webhook.Id, webhook.Cause, webhook.ObjectId);

            // La firma è obbligatoria: senza, chiunque conosca l'URL può fingersi Card Trader
            // e, dato che la vendita scala subito la giacenza, alterare l'inventario a piacere.
            // Documentazione ufficiale Card Trader: l'header si chiama "Signature", non
            // "X-Signature" — con il nome sbagliato la verifica non ha mai potuto funzionare.
            var signatureHeader = Request.Headers["Signature"].FirstOrDefault();
            if (string.IsNullOrEmpty(signatureHeader))
            {
                _logger.LogWarning("Webhook {WebhookId} rifiutato: header Signature mancante", webhook.Id);
                return Unauthorized("Signature header is required");
            }

            // [EnableRequestBodyBuffering] ha reso lo stream riavvolgibile prima del model
            // binding, quindi qui la rilettura integrale del corpo restituisce davvero il payload.
            Request.Body.Position = 0;
            var requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
            Request.Body.Position = 0;

            if (!_signatureVerificationService.VerifyWebhookSignature(requestBody, signatureHeader))
            {
                _logger.LogWarning("Webhook signature verification failed for webhook {WebhookId}", webhook.Id);
                return Unauthorized("Webhook signature verification failed");
            }

            // Create and send the MediatR command to process the webhook
            var command = new ProcessCardTraderWebhookCommand(
                webhookId: webhook.Id,
                cause: webhook.Cause,
                objectId: webhook.ObjectId,
                mode: webhook.Mode,
                data: webhook.Data);

            await _mediator.Send(command);

            _logger.LogInformation("Webhook {WebhookId} processed successfully", webhook.Id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook event");
            return StatusCode(StatusCodes.Status500InternalServerError, "Error processing webhook");
        }
    }
}
