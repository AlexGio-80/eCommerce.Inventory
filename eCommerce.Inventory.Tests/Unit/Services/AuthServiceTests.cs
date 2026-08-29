using System.IdentityModel.Tokens.Jwt;
using System.Text;
using eCommerce.Inventory.Application.DTOs;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using eCommerce.Inventory.Infrastructure.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace eCommerce.Inventory.Tests.Unit.Services;

public class AuthServiceTests
{
    private const string SecretKey = "chiave-di-test-lunga-almeno-32-byte-per-hmac";
    private const string Issuer = "eCommerce.Inventory.Tests";
    private const string Audience = "eCommerce.Inventory.Tests";
    private const string CurrentPassword = "password-attuale";

    private readonly ApplicationDbContext _context;
    private readonly AuthService _service;

    public AuthServiceTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new ApplicationDbContext(options);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:SecretKey"] = SecretKey,
                ["JwtSettings:Issuer"] = Issuer,
                ["JwtSettings:Audience"] = Audience
            })
            .Build();

        _service = new AuthService(_context, configuration);

        _context.Users.Add(new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(CurrentPassword),
            Role = "Admin"
        });
        _context.SaveChanges();
    }

    /// <summary>
    /// Il criterio di autorizzazione globale richiede il ruolo Admin su ogni endpoint: se il
    /// ruolo non arrivasse nella forma che <c>RequireRole</c> legge, l'unico utente resterebbe
    /// chiuso fuori dalla propria applicazione. Questo test valida il token con gli stessi
    /// parametri di <c>Program.cs</c> e controlla il ruolo come lo controlla ASP.NET.
    /// </summary>
    [Fact]
    public async Task LoginAsync_IlTokenPortaIlRuoloRiconosciutoDaRequireRole()
    {
        var result = await _service.LoginAsync(new LoginDto { Username = "admin", Password = CurrentPassword });

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SecretKey))
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(result.Token, validationParameters, out _);

        principal.Identity!.IsAuthenticated.Should().BeTrue();
        principal.Identity.Name.Should().Be("admin");
        principal.IsInRole("Admin").Should().BeTrue();
    }

    [Fact]
    public async Task LoginAsync_PasswordSbagliata_Rifiuta()
    {
        var act = () => _service.LoginAsync(new LoginDto { Username = "admin", Password = "sbagliata" });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ChangePasswordAsync_ConPasswordAttualeCorretta_SostituisceIlHash()
    {
        await _service.ChangePasswordAsync("admin", new ChangePasswordDto
        {
            CurrentPassword = CurrentPassword,
            NewPassword = "una-password-nuova-e-lunga"
        });

        var user = await _context.Users.SingleAsync(u => u.Username == "admin");
        BCrypt.Net.BCrypt.Verify("una-password-nuova-e-lunga", user.PasswordHash).Should().BeTrue();
        BCrypt.Net.BCrypt.Verify(CurrentPassword, user.PasswordHash).Should().BeFalse();
    }

    /// <summary>
    /// Senza questa verifica un token rubato basterebbe a cambiare la password e a chiudere
    /// fuori il proprietario dell'account.
    /// </summary>
    [Fact]
    public async Task ChangePasswordAsync_SenzaLaPasswordAttuale_Rifiuta()
    {
        var act = () => _service.ChangePasswordAsync("admin", new ChangePasswordDto
        {
            CurrentPassword = "sbagliata",
            NewPassword = "una-password-nuova-e-lunga"
        });

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task ChangePasswordAsync_PasswordNuovaTroppoCorta_Rifiuta()
    {
        var act = () => _service.ChangePasswordAsync("admin", new ChangePasswordDto
        {
            CurrentPassword = CurrentPassword,
            NewPassword = "corta"
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
