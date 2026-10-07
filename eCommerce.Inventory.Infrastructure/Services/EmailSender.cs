using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Invio di email in uscita via SMTP, per gli avvisi sugli acquisti. Solo in uscita: nessuna porta
/// da aprire sul router.
///
/// Configurazione nella sezione <c>Email</c> (credenziali solo in <c>appsettings.Production.json</c>,
/// che non è nel repository). Con Gmail serve una "password per le app" (account con verifica in due
/// passaggi), non la password dell'account: Host <c>smtp.gmail.com</c>, Port 587, EnableSsl true.
/// </summary>
public interface IEmailSender
{
    bool IsConfigured { get; }

    /// <exception cref="InvalidOperationException">Se l'invio non è configurato.</exception>
    Task SendAsync(string subject, string htmlBody, CancellationToken cancellationToken = default);
}

public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    private string? Get(string key) => _configuration[$"Email:{key}"];

    public bool IsConfigured =>
        _configuration.GetValue("Email:Enabled", false)
        && !string.IsNullOrWhiteSpace(Get("Host"))
        && !string.IsNullOrWhiteSpace(Get("From"))
        && !string.IsNullOrWhiteSpace(Get("To"));

    public async Task SendAsync(string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Invio email non configurato: compilare la sezione Email (Enabled, Host, From, To) nella configurazione");

        using var message = new MailMessage(Get("From")!, Get("To")!)
        {
            Subject = subject,
            Body = htmlBody,
            IsBodyHtml = true
        };

        using var client = new SmtpClient(Get("Host"), _configuration.GetValue("Email:Port", 587))
        {
            EnableSsl = _configuration.GetValue("Email:EnableSsl", true),
            DeliveryMethod = SmtpDeliveryMethod.Network
        };

        if (!string.IsNullOrWhiteSpace(Get("UserName")))
            client.Credentials = new NetworkCredential(Get("UserName"), Get("Password"));

        await client.SendMailAsync(message, cancellationToken);
    }
}
