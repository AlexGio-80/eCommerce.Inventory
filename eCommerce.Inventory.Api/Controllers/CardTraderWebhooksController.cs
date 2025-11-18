using eCommerce.Inventory.Application.Commands;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.DTOs;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace eCommerce.Inventory.Api.Controllers;

/// <summary>
/// Controller for handling Card Trader webhook events
/// Processes order creation, updates, and deletion notifications
/// </summary>
[ApiController]
[Route("api/[controller]")]
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

            // Verify webhook signature if present in request
            var signatureHeader = Request.Headers["X-Signature"].FirstOrDefault();
            if (!string.IsNullOrEmpty(signatureHeader))
            {
                // Read request body for signature verification
                Request.EnableBuffering();
                var requestBody = await new StreamReader(Request.Body).ReadToEndAsync();
                Request.Body.Position = 0;

                if (!_signatureVerificationService.VerifyWebhookSignature(requestBody, signatureHeader))
                {
                    _logger.LogWarning("Webhook signature verification failed for webhook {WebhookId}", webhook.Id);
                    return Unauthorized("Webhook signature verification failed");
                }
            }
            else
            {
                _logger.LogWarning("X-Signature header missing from webhook {WebhookId}", webhook.Id);
                // Optionally, you can require signature verification. For now, we'll continue processing.
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
