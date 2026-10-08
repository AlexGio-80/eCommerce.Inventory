using System.Globalization;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.SecretLair;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Monitoraggio del negozio Secret Lair di Wizards (Fase 2): a ogni lettura aggiorna prezzo, scorte e
/// stato dei prodotti, segna quelli nuovi, esauriti e tolti dal negozio, e legge l'elenco delle carte
/// dei prodotti che non lo hanno ancora. Per ogni drop nuovo un avviso, nella campanella e via email.
///
/// La prima lettura in assoluto non genera avvisi: tutto il catalogo sarebbe "nuovo".
/// </summary>
public class SecretLairShopMonitorService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly ISecretLairShopClient _client;
    private readonly AlertService _alerts;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SecretLairShopMonitorService> _logger;

    public SecretLairShopMonitorService(
        ApplicationDbContext db, ISecretLairShopClient client, AlertService alerts,
        IConfiguration configuration, ILogger<SecretLairShopMonitorService> logger)
    {
        _db = db;
        _client = client;
        _alerts = alerts;
        _configuration = configuration;
        _logger = logger;
    }

    /// <exception cref="InvalidOperationException">Una lettura è già in corso.</exception>
    public async Task<SecretLairShopRun> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("La lettura del negozio Secret Lair è già in corso");

        var run = new SecretLairShopRun { Outcome = SecretLairShopRunOutcome.Running };
        try
        {
            _db.SecretLairShopRuns.Add(run);
            await _db.SaveChangesAsync(cancellationToken);

            var catalog = await _client.GetCatalogAsync(cancellationToken);
            if (catalog.Count == 0)
                throw new FormatException("Il catalogo del negozio è vuoto: l'interfaccia potrebbe essere cambiata");

            var now = DateTime.UtcNow;
            var firstRun = !await _db.SecretLairShopProducts.AnyAsync(cancellationToken);
            var existing = await _db.SecretLairShopProducts.ToDictionaryAsync(p => p.WizardsProductId, cancellationToken);
            var fresh = new List<SecretLairShopProduct>();

            foreach (var item in catalog)
            {
                if (!existing.TryGetValue(item.ProductId, out var product))
                {
                    product = new SecretLairShopProduct { WizardsProductId = item.ProductId, FirstSeenAt = now };
                    _db.SecretLairShopProducts.Add(product);
                    fresh.Add(product);
                }

                product.RefId = item.RefId;
                product.Title = item.Title;
                product.DropName = item.DropName;
                product.IsFoil = item.IsFoil;
                product.Price = item.Price;
                product.Stock = item.Stock;
                product.IsPreorder = item.IsPreorder;
                product.LimitPerCustomer = item.LimitPerCustomer;
                product.ReleaseDate = item.ReleaseDate;
                product.SaleStart = item.SaleStart;
                product.SaleEnd = item.SaleEnd;
                product.LastSeenAt = now;
                product.RemovedAt = null;

                if (item.Stock is <= 0) product.SoldOutAt ??= now;
                else if (item.Stock is > 0) product.SoldOutAt = null;
            }

            var seen = catalog.Select(i => i.ProductId).ToHashSet();
            foreach (var gone in existing.Values.Where(p => !seen.Contains(p.WizardsProductId) && p.RemovedAt == null))
                gone.RemovedAt = now;

            await _db.SaveChangesAsync(cancellationToken);

            run.Products = catalog.Count;
            run.NewProducts = fresh.Count;
            run.ContentsFetched = await FetchContentsAsync(cancellationToken);

            if (!firstRun && fresh.Count > 0) await NotifyNewDropsAsync(fresh, cancellationToken);

            run.Outcome = SecretLairShopRunOutcome.Succeeded;
            run.Message = firstRun
                ? $"Prima lettura: {catalog.Count} prodotti registrati senza avvisi"
                : $"{catalog.Count} prodotti, {fresh.Count} nuovi, carte lette per {run.ContentsFetched}";
            _logger.LogInformation("Negozio Secret Lair: {Message}", run.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            run.Outcome = SecretLairShopRunOutcome.Failed;
            run.Message = Truncate($"{ex.GetType().Name}: {ex.Message}", 2000);
            _logger.LogError(ex, "Lettura del negozio Secret Lair fallita");
        }
        finally
        {
            run.CompletedAt = DateTime.UtcNow;
            _db.ChangeTracker.Clear();
            _db.SecretLairShopRuns.Update(run);
            await _db.SaveChangesAsync(CancellationToken.None);
            Gate.Release();
        }

        return run;
    }

    /// <summary>
    /// Elenco delle carte per i prodotti che non lo hanno, una pagina alla volta e con una pausa fra
    /// l'una e l'altra; al più <c>SecretLair:Monitor:ContentsPerRun</c> per lettura, prima quelli in
    /// vendita. Al primo avvio il catalogo intero si completa in qualche giro.
    /// </summary>
    private async Task<int> FetchContentsAsync(CancellationToken cancellationToken)
    {
        var limit = _configuration.GetValue("SecretLair:Monitor:ContentsPerRun", 30);
        var pending = await _db.SecretLairShopProducts
            .Where(p => p.ContentsFetchedAt == null && p.RemovedAt == null)
            .OrderByDescending(p => p.Stock > 0 || p.IsPreorder)
            .ThenByDescending(p => p.FirstSeenAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var fetched = 0;
        foreach (var product in pending)
        {
            try
            {
                var lines = await _client.GetContentsAsync(product.WizardsProductId, cancellationToken);
                _db.SecretLairShopCards.RemoveRange(_db.SecretLairShopCards.Where(c => c.SecretLairShopProductId == product.Id));
                foreach (var line in lines)
                {
                    _db.SecretLairShopCards.Add(new SecretLairShopCard
                    {
                        SecretLairShopProductId = product.Id,
                        Quantity = line.Quantity,
                        CardName = Truncate(line.CardName, 200),
                        DisplayName = line.DisplayName is null ? null : Truncate(line.DisplayName, 200)
                    });
                }
                product.ContentsFetchedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(cancellationToken);
                fetched++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una pagina che non si legge non ferma le altre: si riprova al giro dopo.
                _logger.LogWarning(ex, "Carte del prodotto Secret Lair {Id} non lette", product.WizardsProductId);
            }

            await Task.Delay(TimeSpan.FromSeconds(_configuration.GetValue("SecretLair:Monitor:DelaySeconds", 2)), cancellationToken);
        }

        return fetched;
    }

    /// <summary>Un avviso per drop: le edizioni normale e foil dello stesso drop vanno insieme.</summary>
    private async Task NotifyNewDropsAsync(List<SecretLairShopProduct> fresh, CancellationToken cancellationToken)
    {
        var ids = fresh.Select(p => p.Id).ToList();
        var cards = await _db.SecretLairShopCards.AsNoTracking()
            .Where(c => ids.Contains(c.SecretLairShopProductId))
            .ToListAsync(cancellationToken);
        var sendEmail = _configuration.GetValue("SecretLair:Monitor:EmailNewDrops", true);
        var euro = CultureInfo.GetCultureInfo("it-IT");

        foreach (var drop in fresh.GroupBy(p => p.DropName ?? p.Title))
        {
            var lines = drop
                .OrderBy(p => p.IsFoil)
                .Select(p => $"• {p.Title} — {p.Price.ToString("C", euro)}" +
                             (p.IsPreorder ? " (preordine)" : "") +
                             (p.LimitPerCustomer is { } limit ? $", max {limit} a cliente" : ""))
                .ToList();

            var dropCards = cards.Where(c => drop.Any(p => p.Id == c.SecretLairShopProductId))
                .Select(c => c.CardName).Distinct().ToList();
            if (dropCards.Count > 0) lines.Add("Carte: " + string.Join(", ", dropCards));
            lines.Add($"https://secretlair.wizards.com/eu/product/{drop.First().WizardsProductId}");

            await _alerts.NotifyAsync($"Nuovo drop Secret Lair: {drop.Key}", string.Join("\n", lines), sendEmail, cancellationToken);
        }
    }

    /// <summary>Prodotti del negozio, prima quelli ancora in catalogo, con l'esito delle ultime letture.</summary>
    public async Task<SecretLairShopView> GetAsync(CancellationToken cancellationToken = default)
    {
        var products = await _db.SecretLairShopProducts.AsNoTracking()
            .Include(p => p.Cards)
            .OrderBy(p => p.RemovedAt != null)
            .ThenByDescending(p => p.FirstSeenAt)
            .ThenBy(p => p.DropName)
            .ToListAsync(cancellationToken);

        var runs = await _db.SecretLairShopRuns.AsNoTracking()
            .OrderByDescending(r => r.StartedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        return new SecretLairShopView(
            products.Select(p => new SecretLairShopProductDto(
                p.WizardsProductId, p.Title, p.DropName, p.IsFoil, p.Price, p.Stock, p.IsPreorder, p.LimitPerCustomer,
                p.ReleaseDate, p.SaleStart, p.SaleEnd, p.FirstSeenAt, p.LastSeenAt, p.SoldOutAt, p.RemovedAt,
                StatusOf(p), p.ContentsFetchedAt != null,
                p.Cards.OrderBy(c => c.Id).Select(c => new SecretLairShopCardDto(c.Quantity, c.CardName, c.DisplayName)).ToList(),
                $"https://secretlair.wizards.com/eu/product/{p.WizardsProductId}")).ToList(),
            runs.Select(r => new SecretLairShopRunDto(r.StartedAt, r.CompletedAt, r.Outcome.ToString(), r.Products, r.NewProducts, r.ContentsFetched, r.Message)).ToList(),
            _configuration.GetValue("SecretLair:Monitor:Enabled", false));
    }

    public static string StatusOf(SecretLairShopProduct p) =>
        p.RemovedAt != null ? "Tolto dal negozio"
        : p.Stock is <= 0 ? "Esaurito"
        : p.IsPreorder ? "Preordine"
        : p.SaleStart > DateTime.Now ? "In arrivo"
        : "Disponibile";

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

public record SecretLairShopCardDto(int Quantity, string CardName, string? DisplayName);

public record SecretLairShopProductDto(
    string WizardsProductId,
    string Title,
    string? DropName,
    bool IsFoil,
    decimal Price,
    int? Stock,
    bool IsPreorder,
    int? LimitPerCustomer,
    DateTimeOffset? ReleaseDate,
    DateTime? SaleStart,
    DateTime? SaleEnd,
    DateTime FirstSeenAt,
    DateTime LastSeenAt,
    DateTime? SoldOutAt,
    DateTime? RemovedAt,
    string Status,
    bool ContentsKnown,
    List<SecretLairShopCardDto> Cards,
    string Url);

public record SecretLairShopRunDto(DateTime StartedAt, DateTime? CompletedAt, string Outcome, int Products, int NewProducts, int ContentsFetched, string? Message);

/// <param name="MonitorEnabled">False se la lettura automatica è disattivata da configurazione.</param>
public record SecretLairShopView(List<SecretLairShopProductDto> Products, List<SecretLairShopRunDto> Runs, bool MonitorEnabled);
