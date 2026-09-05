using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using eCommerce.Inventory.Api.Controllers;
using eCommerce.Inventory.Application.Commands;
using eCommerce.Inventory.Infrastructure.ExternalServices.CardTrader.Services;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace eCommerce.Inventory.Tests.Integration.Controllers;

/// <summary>
/// Test HTTP end-to-end (TestServer, non solo unit test sulla firma): esercitano davvero
/// il model binding di [FromBody] seguito dalla rilettura del corpo per la verifica HMAC,
/// l'unico modo per cogliere un bug di ordinamento fra EnableBuffering() e model binding.
/// </summary>
public class CardTraderWebhooksControllerHttpIntegrationTests
{
    private const string SharedSecret = "test-shared-secret";
    private const string WebhooksEndpoint = "/api/CardTraderWebhooks/events";

    private static (TestServer Server, Mock<IMediator> MediatorMock) CreateServer()
    {
        var mediatorMock = new Mock<IMediator>();
        mediatorMock
            .Setup(m => m.Send(It.IsAny<ProcessCardTraderWebhookCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MediatR.Unit.Value);

        var builder = new WebHostBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CardTraderApi:SharedSecret"] = SharedSecret
                });
            })
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton(mediatorMock.Object);
                services.AddSingleton<WebhookSignatureVerificationService>();
                services.AddControllers().AddApplicationPart(typeof(CardTraderWebhooksController).Assembly);
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(endpoints => endpoints.MapControllers());
            });

        return (new TestServer(builder), mediatorMock);
    }

    private static string GenerateSignature(string payload, string sharedSecret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(sharedSecret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    private static string BuildPayload() => JsonSerializer.Serialize(new
    {
        id = "webhook-123",
        time = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        cause = "order.create",
        object_class = "Order",
        object_id = 12345,
        mode = "test",
        data = new
        {
            id = 12345,
            code = "ORD-123",
            transaction_code = "TXN-123",
            state = "new",
            buyer = new { id = 1, username = "buyer1" },
            seller_total = new { cents = 9999, currency = "EUR" },
            seller_fee_amount = new { cents = 500, currency = "EUR" },
            seller_subtotal = new { cents = 9499, currency = "EUR" },
            order_shipping_address = new
            {
                name = "Mario Rossi",
                street = "Via Roma 1",
                zip = "00100",
                city = "Roma",
                state_or_province = "RM",
                country_code = "IT",
                country = "Italia"
            },
            order_billing_address = new
            {
                name = "Mario Rossi",
                street = "Via Roma 1",
                zip = "00100",
                city = "Roma",
                state_or_province = "RM",
                country_code = "IT",
                country = "Italia"
            },
            order_items = Array.Empty<object>()
        }
    });

    [Fact]
    public async Task HandleWebhookEvent_MissingSignatureHeader_ReturnsUnauthorized_AndDoesNotProcess()
    {
        var (server, mediatorMock) = CreateServer();
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            WebhooksEndpoint,
            new StringContent(BuildPayload(), Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, body);
        mediatorMock.Verify(
            m => m.Send(It.IsAny<ProcessCardTraderWebhookCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleWebhookEvent_InvalidSignature_ReturnsUnauthorized_AndDoesNotProcess()
    {
        var (server, mediatorMock) = CreateServer();
        using var client = server.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Post, WebhooksEndpoint)
        {
            Content = new StringContent(BuildPayload(), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Signature", "clearly-not-valid");

        var response = await client.SendAsync(request);

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, body);
        mediatorMock.Verify(
            m => m.Send(It.IsAny<ProcessCardTraderWebhookCommand>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task HandleWebhookEvent_ValidSignature_IsProcessed()
    {
        var (server, mediatorMock) = CreateServer();
        using var client = server.CreateClient();
        var payload = BuildPayload();

        var request = new HttpRequestMessage(HttpMethod.Post, WebhooksEndpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Signature", GenerateSignature(payload, SharedSecret));

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        mediatorMock.Verify(
            m => m.Send(It.IsAny<ProcessCardTraderWebhookCommand>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
