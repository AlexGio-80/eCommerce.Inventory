using System.Globalization;
using System.Net;
using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Avvisi sugli acquisti di sigillati (Fase 4 dell'analisi acquisti): valuta le regole sui dati del
/// giorno (listino Cardmarket, storico giornaliero dei sigillati, classifica delle opportunità).
///
/// Una regola vale per un prodotto o per tutti quelli che passano i suoi filtri. Per ogni regola,
/// i prodotti per cui la condizione è diventata vera in questo giro diventano un unico avviso con
/// l'elenco; l'email è un unico riepilogo per giro.
/// </summary>
public class AlertService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly CultureInfo Italian = CultureInfo.GetCultureInfo("it-IT");

    /// <summary>Prodotti elencati per esteso in un avviso; gli altri si riassumono con il numero.</summary>
    private const int MaxListedProducts = 15;

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
            var context = await LoadContextAsync(rules, cancellationToken);
            var created = new List<AlertNotification>();

            foreach (var rule in rules)
            {
                var current = Matches(rule, context);
                var previous = await _db.AlertRuleMatches.Where(m => m.AlertRuleId == rule.Id).ToListAsync(cancellationToken);
                var previousIds = previous.Select(m => m.SealedProductId).ToHashSet();

                var fresh = current.Where(m => !previousIds.Contains(m.ProductId)).ToList();
                foreach (var match in fresh)
                    _db.AlertRuleMatches.Add(new AlertRuleMatch { AlertRuleId = rule.Id, SealedProductId = match.ProductId });

                if (fresh.Count > 0)
                {
                    var notification = BuildNotification(rule, fresh);
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

    /// <summary>
    /// Avviso che non nasce da una regola (es. un drop nuovo nel negozio Secret Lair): campanella e,
    /// se richiesta, email.
    /// </summary>
    public async Task NotifyAsync(string title, string message, bool sendEmail, CancellationToken cancellationToken = default)
    {
        var notification = new AlertNotification
        {
            Title = Truncate(title, 300),
            Message = Truncate(message, 2000),
            EmailRequested = sendEmail
        };
        _db.AlertNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);

        if (sendEmail) await SendDigestAsync(new List<AlertNotification> { notification }, cancellationToken);
    }

    private static AlertNotification BuildNotification(AlertRule rule, List<Match> fresh)
    {
        var ordered = fresh.OrderByDescending(m => m.SortKey).ToList();
        var single = ordered.Count == 1 ? ordered[0] : null;
        var sets = ordered.Select(m => m.SetCode).Distinct().ToList();

        var lines = ordered.Take(MaxListedProducts).Select(m => single != null ? m.Detail : $"• {m.ProductName}: {m.Detail}").ToList();
        if (ordered.Count > MaxListedProducts)
            lines.Add($"… e altri {ordered.Count - MaxListedProducts} prodotti (scheda Opportunità o Avvisi della pagina Acquisti)");

        return new AlertNotification
        {
            AlertRuleId = rule.Id,
            SealedProductId = single?.ProductId,
            SetCode = sets.Count == 1 ? sets[0] : null,
            Title = Truncate(single != null ? $"{rule.Name}: {single.ProductName}" : $"{rule.Name}: {ordered.Count} prodotti", 300),
            Message = Truncate(string.Join("\n", lines), 2000),
            EmailRequested = rule.SendEmail
        };
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
        if (input.Type == AlertRuleType.PriceAtLow && input.Threshold < 7)
            throw new ArgumentException("Per \"prezzo al minimo\" la soglia sono i giorni di storico: almeno 7");

        SealedProduct? product = null;
        if (input.SealedProductId is { } productId)
        {
            product = await _db.SealedProducts.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
                      ?? throw new ArgumentException($"Prodotto {productId} inesistente");
            if (product.CardmarketId == null && input.Type != AlertRuleType.OpeningOpportunity)
                throw new ArgumentException($"\"{product.Name}\" non ha un prodotto Cardmarket abbinato: il prezzo non è controllabile");
        }

        AlertRule rule;
        if (id is { } existingId)
        {
            rule = await _db.AlertRules.FirstOrDefaultAsync(r => r.Id == existingId, cancellationToken)
                   ?? throw new ArgumentException($"Regola {existingId} inesistente");

            // Cambiando la condizione lo stato precedente non vale più: si riparte da zero.
            if (rule.Type != input.Type || rule.SealedProductId != product?.Id || rule.Threshold != input.Threshold
                || rule.UseLowPrice != input.UseLowPrice || rule.SetCode != Clean(input.SetCode)?.ToUpperInvariant()
                || rule.Category != Clean(input.Category) || rule.Subtype != Clean(input.Subtype)
                || rule.RecentReleaseDays != input.RecentReleaseDays)
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
        // Con un prodotto preciso i filtri non servono: si azzerano per non lasciare condizioni nascoste.
        rule.SetCode = product == null ? Clean(input.SetCode)?.ToUpperInvariant() : null;
        rule.Category = product == null ? Clean(input.Category) : null;
        rule.Subtype = product == null ? Clean(input.Subtype) : null;
        rule.RecentReleaseDays = product == null && input.RecentReleaseDays is > 0 ? input.RecentReleaseDays : null;
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
        r.Id, r.Name, r.Type, r.SealedProductId, r.SealedProduct?.Name, r.SetCode, r.Category, r.Subtype, r.RecentReleaseDays,
        r.Threshold, r.UseLowPrice, r.IsActive, r.SendEmail, r.LastEvaluatedAt, matching);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // ---------------------------------------------------------------------------------------------
    // Valutazione

    private record ProductInfo(int Id, string Name, string? Category, string? Subtype, string MainSetCode, DateOnly? ReleaseDate, int? CardmarketId);

    private record EvaluationContext(
        Dictionary<int, ProductInfo> Products,
        Dictionary<int, CardmarketLatestPrice> LatestPrices,
        Dictionary<int, decimal?> TrendWeekAgo,
        Dictionary<int, List<(DateOnly Date, decimal Trend)>> History,
        Dictionary<int, DateOnly> FirstSeen,
        List<SealedOpportunity> Opportunities);

    private async Task<EvaluationContext> LoadContextAsync(List<AlertRule> rules, CancellationToken cancellationToken)
    {
        var sets = await _db.MtgjsonSets.AsNoTracking().Select(s => new { s.Code, s.ParentCode, s.ReleaseDate }).ToListAsync(cancellationToken);
        var setByCode = sets.ToDictionary(s => s.Code);
        string MainOf(string code) =>
            setByCode.TryGetValue(code, out var s) && s.ParentCode != null && setByCode.ContainsKey(s.ParentCode) ? s.ParentCode : code;

        // I case si escludono: non si comprano, e il loro prezzo Cardmarket è spesso abbinato male.
        var products = (await _db.SealedProducts.AsNoTracking()
                .Where(p => p.Category == null || !p.Category.EndsWith("_case"))
                .Select(p => new { p.Id, p.Name, p.Category, p.Subtype, p.SetCode, p.CardmarketId })
                .ToListAsync(cancellationToken))
            .Select(p =>
            {
                var main = MainOf(p.SetCode);
                return new ProductInfo(p.Id, p.Name, p.Category, p.Subtype, main,
                    setByCode.TryGetValue(main, out var s) ? s.ReleaseDate : null, p.CardmarketId);
            })
            .ToDictionary(p => p.Id);

        var latest = await _db.CardmarketLatestPrices.AsNoTracking()
            .Where(p => _db.SealedProducts.Any(s => s.CardmarketId == p.IdProduct))
            .ToDictionaryAsync(p => p.IdProduct, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekAgo = today.AddDays(-7);
        var trendWeekAgo = await _db.CardmarketPriceSnapshots.AsNoTracking()
            .Where(s => s.Date == weekAgo && _db.SealedProducts.Any(p => p.CardmarketId == s.IdProduct))
            .ToDictionaryAsync(s => s.IdProduct, s => s.Trend, cancellationToken);

        // Storico per "prezzo al minimo": solo se qualche regola lo usa, ed è il più lungo richiesto.
        var lowWindow = rules.Where(r => r.Type == AlertRuleType.PriceAtLow).Select(r => (int)r.Threshold).DefaultIfEmpty(0).Max();
        var history = new Dictionary<int, List<(DateOnly, decimal)>>();
        var firstSeen = new Dictionary<int, DateOnly>();
        if (lowWindow > 0)
        {
            var from = today.AddDays(-lowWindow);
            history = (await _db.CardmarketPriceSnapshots.AsNoTracking()
                    .Where(s => s.Date >= from && s.Trend != null && _db.SealedProducts.Any(p => p.CardmarketId == s.IdProduct))
                    .Select(s => new { s.IdProduct, s.Date, Trend = s.Trend!.Value })
                    .ToListAsync(cancellationToken))
                .GroupBy(s => s.IdProduct)
                .ToDictionary(g => g.Key, g => g.Select(s => (s.Date, s.Trend)).ToList());
            // Prima rilevazione di ogni prodotto: il minimo di una finestra ha senso solo se lo storico
            // del prodotto parte prima della finestra.
            firstSeen = await _db.CardmarketPriceSnapshots.AsNoTracking()
                .Where(s => _db.SealedProducts.Any(p => p.CardmarketId == s.IdProduct))
                .GroupBy(s => s.IdProduct)
                .Select(g => new { IdProduct = g.Key, First = g.Min(s => s.Date) })
                .ToDictionaryAsync(x => x.IdProduct, x => x.First, cancellationToken);
        }

        var lastDate = await _db.SealedOpportunities.Select(o => (DateOnly?)o.Date).MaxAsync(cancellationToken);
        var opportunities = lastDate == null
            ? new List<SealedOpportunity>()
            : await _db.SealedOpportunities.AsNoTracking().Where(o => o.Date == lastDate).ToListAsync(cancellationToken);

        return new EvaluationContext(products, latest, trendWeekAgo, history, firstSeen, opportunities);
    }

    /// <param name="SortKey">Per ordinare l'elenco nell'avviso: prima i prodotti più interessanti.</param>
    private record Match(int ProductId, string ProductName, string SetCode, string Detail, decimal SortKey);

    /// <summary>Prodotti a cui si applica la regola: quello scelto, o tutti quelli che passano i filtri.</summary>
    private static IEnumerable<ProductInfo> InScope(AlertRule rule, EvaluationContext context)
    {
        if (rule.SealedProductId is { } id)
            return context.Products.TryGetValue(id, out var p) ? new[] { p } : Array.Empty<ProductInfo>();

        var today = DateOnly.FromDateTime(DateTime.Today);
        return context.Products.Values.Where(p =>
            (rule.SetCode == null || string.Equals(p.MainSetCode, rule.SetCode, StringComparison.OrdinalIgnoreCase))
            && (rule.Category == null || p.Category == rule.Category)
            && (rule.Subtype == null || p.Subtype == rule.Subtype)
            && (rule.RecentReleaseDays == null || (p.ReleaseDate is { } release && release >= today.AddDays(-rule.RecentReleaseDays.Value))));
    }

    private static List<Match> Matches(AlertRule rule, EvaluationContext context)
    {
        var result = new List<Match>();
        var today = DateOnly.FromDateTime(DateTime.Today);

        if (rule.Type == AlertRuleType.OpeningOpportunity)
        {
            var scope = InScope(rule, context).Select(p => p.Id).ToHashSet();
            foreach (var o in context.Opportunities.Where(o => o.Decision == "Apri" && o.OpeningRoiPercent >= rule.Threshold && scope.Contains(o.SealedProductId)))
            {
                var info = context.Products[o.SealedProductId];
                result.Add(new Match(info.Id, info.Name, info.MainSetCode,
                    $"aprirlo rende il {o.OpeningRoiPercent!.Value.ToString("+0.0;-0.0", Italian)}% (valore atteso netto {Euro(o.OpenValueCm ?? 0)}, trend {Euro(o.CmTrend ?? 0)})",
                    o.OpeningRoiPercent.Value));
            }
            return result;
        }

        foreach (var info in InScope(rule, context))
        {
            if (info.CardmarketId is not { } cmId || !context.LatestPrices.TryGetValue(cmId, out var price)) continue;

            switch (rule.Type)
            {
                case AlertRuleType.PriceBelow:
                {
                    var value = rule.UseLowPrice ? price.Low : price.Trend;
                    if (value is > 0 && value <= rule.Threshold)
                        result.Add(new Match(info.Id, info.Name, info.MainSetCode,
                            $"{(rule.UseLowPrice ? "prezzo più basso" : "trend")} Cardmarket {Euro(value.Value)}, sotto la soglia di {Euro(rule.Threshold)}",
                            rule.Threshold - value.Value));
                    break;
                }

                case AlertRuleType.PriceDrop:
                {
                    var before = context.TrendWeekAgo.GetValueOrDefault(cmId);
                    if (price.Trend is > 0 && before is > 0)
                    {
                        var drop = (before.Value - price.Trend.Value) / before.Value * 100m;
                        if (drop >= rule.Threshold)
                            result.Add(new Match(info.Id, info.Name, info.MainSetCode,
                                $"trend sceso del {drop.ToString("0.0", Italian)}% in 7 giorni, da {Euro(before.Value)} a {Euro(price.Trend.Value)}",
                                drop));
                    }
                    break;
                }

                case AlertRuleType.PriceAtLow:
                {
                    // Un minimo ha senso solo se lo storico del prodotto copre tutta la finestra: uno
                    // comparso su Cardmarket da pochi giorni avrebbe un "minimo dei 90 giorni" di pochi giorni.
                    var window = (int)rule.Threshold;
                    if (!context.FirstSeen.TryGetValue(cmId, out var first) || first > today.AddDays(-window)) break;
                    if (price.Trend is not > 0 || !context.History.TryGetValue(cmId, out var points)) break;

                    var previousMin = points.Where(p => p.Date < today).Select(p => p.Trend).DefaultIfEmpty(decimal.MaxValue).Min();
                    if (previousMin != decimal.MaxValue && price.Trend.Value < previousMin)
                        result.Add(new Match(info.Id, info.Name, info.MainSetCode,
                            $"trend {Euro(price.Trend.Value)}, il più basso degli ultimi {window} giorni (minimo precedente {Euro(previousMin)})",
                            (previousMin - price.Trend.Value) / previousMin * 100m));
                    break;
                }
            }
        }

        return result;
    }

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
                       $"<li><strong>{WebUtility.HtmlEncode(n.Title)}</strong><br>{WebUtility.HtmlEncode(n.Message).Replace("\n", "<br>")}</li>"))
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

/// <param name="SealedProductId">Un solo prodotto; null = tutti quelli che passano i filtri.</param>
/// <param name="RecentReleaseDays">Solo uscite di non più di tanti giorni fa, comprese quelle in arrivo.</param>
public record AlertRuleInput(
    string Name,
    AlertRuleType Type,
    int? SealedProductId,
    string? SetCode,
    string? Category,
    decimal Threshold,
    bool UseLowPrice,
    bool IsActive,
    bool SendEmail,
    string? Subtype = null,
    int? RecentReleaseDays = null);

/// <param name="MatchingCount">Prodotti per cui la regola è vera dall'ultima valutazione.</param>
public record AlertRuleDto(
    int Id,
    string Name,
    AlertRuleType Type,
    int? SealedProductId,
    string? ProductName,
    string? SetCode,
    string? Category,
    string? Subtype,
    int? RecentReleaseDays,
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
