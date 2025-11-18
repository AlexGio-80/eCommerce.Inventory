using System.Security.Cryptography;
using System.Text;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Tests.Unit.Services;

/// <summary>
/// Unit tests for WebhookSignatureVerificationService
/// Tests HMAC SHA256 signature verification logic
/// </summary>
public class WebhookSignatureVerificationServiceTests
{
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<ILogger<WebhookSignatureVerificationService>> _mockLogger;
    private readonly WebhookSignatureVerificationService _service;
    private const string SharedSecret = "test-shared-secret-key";

    public WebhookSignatureVerificationServiceTests()
    {
        _mockConfiguration = new Mock<IConfiguration>();
        _mockLogger = new Mock<ILogger<WebhookSignatureVerificationService>>();

        // Setup configuration to return shared secret
        _mockConfiguration.Setup(x => x["CardTraderApi:SharedSecret"]).Returns(SharedSecret);

        _service = new WebhookSignatureVerificationService(_mockConfiguration.Object, _mockLogger.Object);
    }

    [Fact]
    public void VerifyWebhookSignature_ValidSignature_ReturnsTrue()
    {
        // Arrange
        const string requestBody = @"{""id"":""webhook-123"",""cause"":""order.create"",""object_id"":12345}";
        var signature = GenerateSignature(requestBody, SharedSecret);

        // Act
        var result = _service.VerifyWebhookSignature(requestBody, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_InvalidSignature_ReturnsFalse()
    {
        // Arrange
        const string requestBody = @"{""id"":""webhook-123"",""cause"":""order.create"",""object_id"":12345}";
        const string wrongSignature = "invalid-signature-value";

        // Act
        var result = _service.VerifyWebhookSignature(requestBody, wrongSignature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_WrongSecret_ReturnsFalse()
    {
        // Arrange
        const string requestBody = @"{""id"":""webhook-123"",""cause"":""order.create"",""object_id"":12345}";
        var wrongSecretSignature = GenerateSignature(requestBody, "wrong-secret");

        // Act
        var result = _service.VerifyWebhookSignature(requestBody, wrongSecretSignature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_EmptyRequestBody_ReturnsFalse()
    {
        // Arrange
        const string signature = "some-signature";

        // Act
        var result = _service.VerifyWebhookSignature(string.Empty, signature);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void VerifyWebhookSignature_LargePayload_VerifiesCorrectly()
    {
        // Arrange
        var largePayload = new string('x', 10000); // 10KB payload
        var signature = GenerateSignature(largePayload, SharedSecret);

        // Act
        var result = _service.VerifyWebhookSignature(largePayload, signature);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void VerifyWebhookSignature_PayloadTampered_ReturnsFalse()
    {
        // Arrange
        const string originalPayload = @"{""id"":""webhook-123"",""total"":99.99}";
        var signature = GenerateSignature(originalPayload, SharedSecret);
        const string tamperedPayload = @"{""id"":""webhook-123"",""total"":199.99}"; // Total changed

        // Act
        var result = _service.VerifyWebhookSignature(tamperedPayload, signature);

        // Assert
        result.Should().BeFalse();
    }

    private static string GenerateSignature(string payload, string sharedSecret)
    {
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret)))
        {
            var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hashBytes);
        }
    }
}
