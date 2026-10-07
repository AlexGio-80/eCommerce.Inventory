using System.Globalization;
using System.Net;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Avvisi sugli acquisti di sigillati (Fase 4 dell'analisi acquisti): valuta le regole sui dati del
/// giorno (listino Cardmarket e classifica delle opportunità), emette un avviso per ogni prodotto
/// per cui una regola è diventata vera e manda un'unica email di riepilogo per giro.
/// </summary>
public class AlertService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    private readonly ApplicationDbContext _db;
    private readonly IEmailSender _email;
    private readonly ILogger<AlertService> _logger;

    public AlertService(ApplicationDbContext db, IEmailSender email, ILogger<AlertService> logger)
    {
        _db = db;
        _email = email;
        _logger = logger;
    }

    /// <exception cref="InvalidOperationException">Se un'altra valutazione è in corso.</exception>
    public async Task<AlertEvaluationResult> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("La valutazione degli avvisi è già in corso");

        try
        {
            var rules = await _db.AlertRules.Where(r => r.IsActive).ToListAsync(cancellationToken);
            var context = await LoadContextAsync(cancellationToken);
            var created = new List<AlertNotification>();

            foreach (var rule in rules)
            {
                var current = Matches(rule, context);
                var previous = await _db.AlertRuleMatches.Where(m => m.AlertRuleId == rule.Id).ToListAsync(cancellationToken);
                var previousIds = previous.Select(m => m.SealedProductId).ToHashSet();

                foreach (var match in current.Where(m => !previousIds.Contains(m.ProductId)))
                {
                    _db.AlertRuleMatches.Add(new AlertRuleMatch { AlertRuleId = rule.Id, SealedProductId = match.ProductId });
                    var notification = new AlertNotification
                    {
                        AlertRuleId = rule.Id,
                        SealedProductId = match.ProductId,
                        SetCode = match.SetCode,
                        Title = Truncate($"{rule.Name}: {match.ProductName}", 300),
                        Message = Truncate(match.Message, 2000),
                        EmailRequested = rule.SendEmail
                    };
                    _db.AlertNotifications.Add(notification);
                    created.Add(notification);
                }

                // Condizione non più vera: se lo ridiventa, l'avviso scatta di nuovo.
                var currentIds = current.Select(m => m.ProductId).ToHashSet();
                _db.AlertRuleMatches.RemoveRange(previous.Where(m => !currentIds.Contains(m.SealedProductId)));

                rule.LastEvaluatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync(cancellationToken);

            var emailed = await SendDigestAsync(created.Where(n => n.EmailRequested).ToList(), cancellationToken);

            _logger.LogInformation("Avvisi acquisti: {Rules} regole valutate, {New} avvisi nuovi, email {Email}",
                rules.Count, created.Count, emailed);

            return new AlertEvaluationResult(rules.Count, created.Count, emailed);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task SendTestEmailAsync(CancellationToken cancellationToken = default) =>
        await _email.SendAsync("Avvisi acquisti — email di prova",
            "<p>Se leggi questo messaggio, l'invio degli avvisi sugli acquisti di eCommerce.Inventory è configurato correttamente.</p>",
            cancellationToken);

    public bool EmailConfigured => _email.IsConfigured;

    public async Task<List<AlertRuleDto>> ListRulesAsync(CancellationToken cancellationToken = default)
    {
        var matches = await _db.AlertRuleMatches.AsNoTracking()
            .GroupBy(m => m.AlertRuleId)
            .Select(g => new { RuleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.RuleId, x => x.Count, cancellationToken);

        return (await _db.AlertRules.AsNoTracking().Include(r => r.SealedProduct).OrderBy(r => r.Name).ToListAsync(cancellationToken))
            .Select(r => MapRule(r, matches.GetValueOrDefault(r.Id)))
            .ToList();
    }

    /// <exception cref="ArgumentException">Dati non validi.</exception>
    public async Task<AlertRuleDto> SaveRuleAsync(int? id, AlertRuleInput input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input.Name)) throw new ArgumentException("Il nome è obbligatorio");
        if (input.Threshold <= 0 && input.Type != AlertRuleType.OpeningOpportunity)
            throw new ArgumentException("La soglia deve essere maggiore di zero");

        SealedProduct? product = null;
        if (input.Type is AlertRuleType.PriceBelow or AlertRuleType.PriceDrop)
        {
            product = await _db.SealedProducts.FirstOrDefaultAsync(p => p.Id == input.SealedProductId, cancellationToken)
                      ?? throw new ArgumentException("Per un avviso sul prezzo serve il prodotto");
            if (product.CardmarketId == null)
                throw new ArgumentException($"\"{product.Name}\" non ha un prodotto Cardmarket abbinato: il prezzo non è controllabile");
        }

        AlertRule rule;
        if (id is { } existingId)
        {
            rule = await _db.AlertRules.FirstOrDefaultAsync(r => r.Id == existingId, cancellationToken)
                   ?? throw new ArgumentException($"Regola {existingId} inesistente");

            // Cambiando la condizione lo stato precedente non vale più: si riparte da zero.
            if (rule.Type != input.Type || rule.SealedProductId != product?.Id || rule.Threshold != input.Threshold
                || rule.UseLowPrice != input.UseLowPrice || rule.SetCode != Clean(input.SetCode) || rule.Category != Clean(input.Category))
            {
                _db.AlertRuleMatches.RemoveRange(await _db.AlertRuleMatches.Where(m => m.AlertRuleId == rule.Id).ToListAsync(cancellationToken));
            }
        }
        else
        {
            rule = new AlertRule();
            _db.AlertRules.Add(rule);
        }

        rule.Name = Truncate(input.Name.Trim(), 200);
        rule.Type = input.Type;
        rule.SealedProductId = product?.Id;
        rule.SetCode = input.Type == AlertRuleType.OpeningOpportunity ? Clean(input.SetCode)?.ToUpperInvariant() : null;
        rule.Category = input.Type == AlertRuleType.OpeningOpportunity ? Clean(input.Category) : null;
        rule.Threshold = input.Threshold;
        rule.UseLowPrice = input.Type == AlertRuleType.PriceBelow && input.UseLowPrice;
        rule.IsActive = input.IsActive;
        rule.SendEmail = input.SendEmail;

        await _db.SaveChangesAsync(cancellationToken);
        rule.SealedProduct = product;
        return MapRule(rule, 0);
    }

    public async Task<bool> DeleteRuleAsync(int id, CancellationToken cancellationToken = default)
    {
        var rule = await _db.AlertRules.FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (rule == null) return false;
        _db.AlertRules.Remove(rule);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AlertNotificationList> ListNotificationsAsync(int take, CancellationToken cancellationToken = default)
    {
        var unread = await _db.AlertNotifications.CountAsync(n => n.ReadAt == null, cancellationToken);
        var items = await _db.AlertNotifications.AsNoTracking()
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(n => new AlertNotificationDto(n.Id, n.AlertRuleId, n.SealedProductId, n.SetCode, n.Title, n.Message,
                n.CreatedAt, n.ReadAt, n.EmailRequested, n.EmailSentAt, n.EmailError))
            .ToListAsync(cancellationToken);
        return new AlertNotificationList(unread, _email.IsConfigured, items);
    }

    /// <summary>Segna come letto un avviso, o tutti se <paramref name="id"/> è null.</summary>
    public async Task MarkReadAsync(int? id, CancellationToken cancellationToken = default)
    {
        var unread = await _db.AlertNotifications
            .Where(n => n.ReadAt == null && (id == null || n.Id == id))
            .ToListAsync(cancellationToken);
        foreach (var n in unread) n.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static AlertRuleDto MapRule(AlertRule r, int matching) => new(
        r.Id, r.Name, r.Type, r.SealedProductId, r.SealedProduct?.Name, r.SetCode, r.Category,
        r.Threshold, r.UseLowPrice, r.IsActive, r.SendEmail, r.LastEvaluatedAt, matching);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private record ProductInfo(int Id, string Name, string? Category, string SetCode, int? CardmarketId);

    private record EvaluationContext(
        Dictionary<int, ProductInfo> Products,
        Dictionary<int, CardmarketLatestPrice> LatestPrices,
        Dictionary<int, decimal?> TrendWeekAgo,
        List<SealedOpportunity> Opportunities,
        Dictionary<string, string> MainSetBySet);

    private async Task<EvaluationContext> LoadContextAsync(CancellationToken cancellationToken)
    {
        var products = await _db.SealedProducts.AsNoTracking()
            .Select(p => new ProductInfo(p.Id, p.Name, p.Category, p.SetCode, p.CardmarketId))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var cmIds = await _db.AlertRules.Where(r => r.IsActive && r.SealedProductId != null)
            .Select(r => r.SealedProduct!.CardmarketId)
            .Where(id => id != null)
            .Select(id => id!.Value)
            .ToListAsync(cancellationToken);

        var latest = await _db.CardmarketLatestPrices.AsNoTracking()
            .Where(p => cmIds.Contains(p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, cancellationToken);

        // Trend di 7 giorni prima dallo storico giornaliero dei sigillati.
        var weekAgo = DateOnly.FromDateTime(DateTime.Today).AddDays(-7);
        var trendWeekAgo = await _db.CardmarketPriceSnapshots.AsNoTracking()
            .Where(s => cmIds.Contains(s.IdProduct) && s.Date == weekAgo)
            .ToDictionaryAsync(s => s.IdProduct, s => s.Trend, cancellationToken);

        var lastDate = await _db.SealedOpportunities.Select(o => (DateOnly?)o.Date).MaxAsync(cancellationToken);
        var opportunities = lastDate == null
            ? new List<SealedOpportunity>()
            : await _db.SealedOpportunities.AsNoTracking().Where(o => o.Date == lastDate).ToListAsync(cancellationToken);

        var sets = await _db.MtgjsonSets.AsNoTracking().Select(s => new { s.Code, s.ParentCode }).ToListAsync(cancellationToken);
        var codes = sets.Select(s => s.Code).ToHashSet();
        var mainSetBySet = sets.ToDictionary(s => s.Code, s => s.ParentCode != null && codes.Contains(s.ParentCode) ? s.ParentCode : s.Code);

        return new EvaluationContext(products, latest, trendWeekAgo, opportunities, mainSetBySet);
    }

    private record Match(int ProductId, string ProductName, string? SetCode, string Message);

    private static List<Match> Matches(AlertRule rule, EvaluationContext context)
    {
        var result = new List<Match>();
        switch (rule.Type)
        {
            case AlertRuleType.PriceBelow:
            {
                if (ProductPrice(rule, context) is not { } product) break;
                var (info, price) = product;
                var value = rule.UseLowPrice ? price.Low : price.Trend;
                if (value is > 0 && value <= rule.Threshold)
                    result.Add(new Match(info.Id, info.Name, MainSet(info, context),
                        $"{(rule.UseLowPrice ? "Prezzo più basso" : "Trend")} Cardmarket {Euro(value.Value)}, sotto la soglia di {Euro(rule.Threshold)}."));
                break;
            }

            case AlertRuleType.PriceDrop:
            {
                if (ProductPrice(rule, context) is not { } product) break;
                var (info, price) = product;
                var before = context.TrendWeekAgo.GetValueOrDefault(info.CardmarketId!.Value);
                if (price.Trend is > 0 && before is > 0)
                {
                    var drop = (before.Value - price.Trend.Value) / before.Value * 100m;
                    if (drop >= rule.Threshold)
                        result.Add(new Match(info.Id, info.Name, MainSet(info, context),
                            $"Trend Cardmarket sceso del {drop.ToString("0.0", Italian)}% in 7 giorni: da {Euro(before.Value)} a {Euro(price.Trend.Value)}."));
                }
                break;
            }

            case AlertRuleType.OpeningOpportunity:
                foreach (var o in context.Opportunities.Where(o => o.Decision == "Apri" && o.OpeningRoiPercent >= rule.Threshold))
                {
                    if (!context.Products.TryGetValue(o.SealedProductId, out var info)) continue;
                    if (rule.SetCode != null && !string.Equals(o.MainSetCode, rule.SetCode, StringComparison.OrdinalIgnoreCase)) continue;
                    if (rule.Category != null && info.Category != rule.Category) continue;
                    result.Add(new Match(info.Id, info.Name, o.MainSetCode,
                        $"Aprirlo rende il {o.OpeningRoiPercent!.Value.ToString("+0.0;-0.0", Italian)}%: valore atteso netto {Euro(o.OpenValueCm ?? 0)} contro un trend di {Euro(o.CmTrend ?? 0)}."));
                }
                break;
        }

        return result;
    }

    private static (ProductInfo Info, CardmarketLatestPrice Price)? ProductPrice(AlertRule rule, EvaluationContext context)
    {
        if (rule.SealedProductId is not { } id || !context.Products.TryGetValue(id, out var info) || info.CardmarketId is not { } cmId)
            return null;
        return context.LatestPrices.TryGetValue(cmId, out var price) ? (info, price) : null;
    }

    private static string MainSet(ProductInfo info, EvaluationContext context) =>
        context.MainSetBySet.GetValueOrDefault(info.SetCode, info.SetCode);

    /// <summary>Un'unica email per giro con tutti gli avvisi nuovi; l'esito resta sull'avviso.</summary>
    private async Task<string> SendDigestAsync(List<AlertNotification> notifications, CancellationToken cancellationToken)
    {
        if (notifications.Count == 0) return "nessun avviso da inviare";
        if (!_email.IsConfigured)
        {
            foreach (var n in notifications) n.EmailError = "Invio email non configurato";
            await _db.SaveChangesAsync(cancellationToken);
            return "non configurata";
        }

        var body = "<p>Nuovi avvisi sugli acquisti di sigillati:</p><ul>"
                   + string.Concat(notifications.Select(n =>
                       $"<li><strong>{WebUtility.HtmlEncode(n.Title)}</strong><br>{WebUtility.HtmlEncode(n.Message)}</li>"))
                   + "</ul><p>Dettagli nella pagina Acquisti di eCommerce.Inventory.</p>";
        var subject = notifications.Count == 1 ? notifications[0].Title : $"{notifications.Count} nuovi avvisi sugli acquisti";

        try
        {
            await _email.SendAsync(Truncate(subject, 200), body, cancellationToken);
            foreach (var n in notifications) n.EmailSentAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return "inviata";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Invio email degli avvisi fallito");
            foreach (var n in notifications) n.EmailError = Truncate($"{ex.GetType().Name}: {ex.Message}", 1000);
            await _db.SaveChangesAsync(cancellationToken);
            return "fallita";
        }
    }

    private static string Euro(decimal value) => value.ToString("C2", Italian);

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

public record AlertEvaluationResult(int RulesEvaluated, int NewNotifications, string Email);

public record AlertRuleInput(
    string Name,
    AlertRuleType Type,
    int? SealedProductId,
    string? SetCode,
    string? Category,
    decimal Threshold,
    bool UseLowPrice,
    bool IsActive,
    bool SendEmail);

/// <param name="MatchingCount">Prodotti per cui la regola è vera dall'ultima valutazione.</param>
public record AlertRuleDto(
    int Id,
    string Name,
    AlertRuleType Type,
    int? SealedProductId,
    string? ProductName,
    string? SetCode,
    string? Category,
    decimal Threshold,
    bool UseLowPrice,
    bool IsActive,
    bool SendEmail,
    DateTime? LastEvaluatedAt,
    int MatchingCount);

public record AlertNotificationDto(
    int Id,
    int? AlertRuleId,
    int? SealedProductId,
    string? SetCode,
    string Title,
    string Message,
    DateTime CreatedAt,
    DateTime? ReadAt,
    bool EmailRequested,
    DateTime? EmailSentAt,
    string? EmailError);

public record AlertNotificationList(int Unread, bool EmailConfigured, List<AlertNotificationDto> Items);
