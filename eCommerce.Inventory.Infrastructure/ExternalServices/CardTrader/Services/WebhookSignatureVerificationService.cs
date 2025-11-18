using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;

/// <summary>
/// Service for verifying Card Trader webhook signatures
/// Implements HMAC SHA256 verification according to Card Trader API specification
/// </summary>
public class WebhookSignatureVerificationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookSignatureVerificationService> _logger;
    private readonly string _sharedSecret;

    public WebhookSignatureVerificationService(
        IConfiguration configuration,
        ILogger<WebhookSignatureVerificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        _sharedSecret = _configuration["CardTraderApi:SharedSecret"]
            ?? throw new InvalidOperationException("CardTraderApi:SharedSecret is not configured");
    }

    /// <summary>
    /// Verify webhook signature using HMAC SHA256
    /// Card Trader signs the request body with the app's shared_secret
    /// Signature is base64 representation of HMAC SHA256 digest
    /// </summary>
    /// <param name="requestBody">The raw request body as string</param>
    /// <param name="signatureHeader">The X-Signature header value from the request</param>
    /// <returns>True if signature is valid, false otherwise</returns>
    public bool VerifyWebhookSignature(string requestBody, string signatureHeader)
    {
        try
        {
            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("Webhook verification failed: request body is empty");
                return false;
            }

            if (string.IsNullOrEmpty(signatureHeader))
            {
                _logger.LogWarning("Webhook verification failed: signature header is missing");
                return false;
            }

            // Compute HMAC SHA256 of the request body using the shared secret
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_sharedSecret)))
            {
                var requestBodyBytes = Encoding.UTF8.GetBytes(requestBody);
                var hashBytes = hmac.ComputeHash(requestBodyBytes);

                // Convert hash to base64
                var computedSignature = Convert.ToBase64String(hashBytes);

                // Compare with received signature (constant-time comparison to prevent timing attacks)
                var isValid = CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(computedSignature),
                    Encoding.UTF8.GetBytes(signatureHeader));

                if (!isValid)
                {
                    _logger.LogWarning(
                        "Webhook signature verification failed. Expected: {ExpectedSignature}, Received: {ReceivedSignature}",
                        "***" /* Masked for security */,
                        "***" /* Masked for security */);
                }
                else
                {
                    _logger.LogInformation("Webhook signature verified successfully");
                }

                return isValid;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during webhook signature verification");
            return false;
        }
    }
}
