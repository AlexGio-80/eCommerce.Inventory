using eCommerce.Inventory.Domain.Entities;
using eCommerce.Inventory.Infrastructure.ExternalServices.MtgJson;
using eCommerce.Inventory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Services;

/// <summary>
/// Import da MTGJSON dei dettagli necessari al valore atteso di un'uscita: carte (con gli id per
/// i prezzi), composizione delle buste e mazzi. Un file per set, qualche MB ciascuno, quindi si
/// importano solo le uscite che servono: a richiesta e, ogni giorno, quelle recenti o in arrivo
/// (così la composizione delle buste di un'uscita in preordine arriva da sola quando MTGJSON la pubblica).
///
/// Ogni set viene sostituito per intero: è un dato di riferimento, non c'è nulla da conservare.
/// </summary>
public class MtgjsonSetDetailImportService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly ApplicationDbContext _db;
    private readonly IMtgJsonSetListClient _client;
    private readonly ILogger<MtgjsonSetDetailImportService> _logger;

    public MtgjsonSetDetailImportService(
        ApplicationDbContext db,
        IMtgJsonSetListClient client,
        ILogger<MtgjsonSetDetailImportService> logger)
    {
        _db = db;
        _client = client;
        _logger = logger;
    }

    /// <summary>Importa il set e i suoi figli, più le carte dei set da cui provengono i suoi mazzi.</summary>
    /// <exception cref="InvalidOperationException">Se un altro import dei dettagli è in corso.</exception>
    public async Task<SetDetailImportResult> ImportGroupAsync(string setCode, CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("Un import dei dati delle buste è già in corso");

        try
        {
            setCode = setCode.ToUpperInvariant();
            var groupCodes = await _db.MtgjsonSets.AsNoTracking()
                .Where(s => s.Code == setCode || s.ParentCode == setCode)
                .Select(s => s.Code)
                .ToListAsync(cancellationToken);
            if (groupCodes.Count == 0) groupCodes.Add(setCode);

            var result = new SetDetailImportResult();
            var deckSourceSets = new HashSet<string>();

            foreach (var code in groupCodes)
            {
                var detail = await _client.GetSetAsync(code, cancellationToken);
                if (detail == null) continue;

                await ReplaceSetAsync(code, detail, includeBoostersAndDecks: true, result, cancellationToken);
                foreach (var source in (detail.Decks ?? new()).SelectMany(d => d.SourceSetCodes ?? new()))
                    deckSourceSets.Add(source.ToUpperInvariant());
            }

            // I mazzi possono contenere ristampe di altri set: senza quelle carte resterebbero senza prezzo.
            foreach (var code in deckSourceSets.Except(groupCodes))
            {
                var detail = await _client.GetSetAsync(code, cancellationToken);
                if (detail == null) continue;
                await ReplaceSetAsync(code, detail, includeBoostersAndDecks: false, result, cancellationToken);
            }

            _logger.LogInformation(
                "Dati delle buste MTGJSON di {Set}: {Sets} set, {Cards} carte, {BoosterTypes} tipi di busta, {Decks} mazzi",
                setCode, result.Sets, result.Cards, result.BoosterTypes, result.Decks);

            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Aggiornamento giornaliero: uscite principali fra 60 giorni fa e 120 giorni da oggi. Ogni giorno
    /// finché MTGJSON non pubblica la composizione delle buste (di solito intorno all'uscita), poi una
    /// volta a settimana.
    /// </summary>
    public async Task<int> ImportRecentAsync(CancellationToken cancellationToken = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var weekAgo = DateTime.UtcNow.AddDays(-7);

        var candidates = await _db.MtgjsonSets.AsNoTracking()
            .Where(s => s.ParentCode == null && s.ReleaseDate != null
                        && s.ReleaseDate >= today.AddDays(-60) && s.ReleaseDate <= today.AddDays(120))
            .Where(s => _db.SealedProducts.Any(p => p.SetCode == s.Code))
            .Where(s => s.DetailImportedAt == null || !s.HasBoosterData || s.DetailImportedAt < weekAgo)
            .Select(s => s.Code)
            .ToListAsync(cancellationToken);

        foreach (var code in candidates)
        {
            await ImportGroupAsync(code, cancellationToken);
        }

        return candidates.Count;
    }

    /// <summary>
    /// Scaricamento graduale per la classifica delle opportunità: ogni giorno un lotto di uscite che
    /// non hanno ancora i dati delle buste, partendo dalle più recenti e solo fra quelle con almeno un
    /// sigillato in vendita su Cardmarket. Scaricarle tutte insieme vorrebbe dire 1-2 GB in un colpo.
    /// Un'uscita che fallisce non ferma le altre.
    /// </summary>
    public async Task<int> ImportPendingAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var sets = await _db.MtgjsonSets.AsNoTracking().ToListAsync(cancellationToken);
        var codes = sets.Select(s => s.Code).ToHashSet();

        var withPrice = (await _db.SealedProducts.AsNoTracking()
                .Where(p => p.CardmarketId != null && _db.CardmarketLatestPrices.Any(c => c.IdProduct == p.CardmarketId && c.Trend > 0))
                .Select(p => p.SetCode)
                .Distinct()
                .ToListAsync(cancellationToken))
            .Select(code => sets.FirstOrDefault(s => s.Code == code))
            .Where(s => s != null)
            .Select(s => s!.ParentCode != null && codes.Contains(s.ParentCode) ? s.ParentCode : s.Code)
            .ToHashSet();

        var pending = sets
            .Where(s => withPrice.Contains(s.Code) && s.DetailImportedAt == null)
            .OrderByDescending(s => s.ReleaseDate)
            .Take(batchSize)
            .Select(s => s.Code)
            .ToList();

        var imported = 0;
        foreach (var code in pending)
        {
            try
            {
                await ImportGroupAsync(code, cancellationToken);
                imported++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Dati delle buste MTGJSON di {Set} non importati", code);
                _db.ChangeTracker.Clear();
            }
        }

        return imported;
    }

    private async Task ReplaceSetAsync(
        string code, MtgJsonSetDetailDto detail, bool includeBoostersAndDecks,
        SetDetailImportResult result, CancellationToken cancellationToken)
    {
        // Le carte si aggiornano invece di sostituirle: cancellare e riaggiungere lo stesso uuid nello
        // stesso salvataggio non è ammesso da EF.
        var existing = await _db.MtgjsonCards.Where(c => c.SetCode == code).ToDictionaryAsync(c => c.Uuid, cancellationToken);

        // Un uuid già importato con un altro codice di set (raro) si salta invece di far fallire l'import.
        var incoming = detail.Cards.Where(c => c.Uuid != Guid.Empty).GroupBy(c => c.Uuid).Select(g => g.First()).ToList();
        var incomingIds = incoming.Select(c => c.Uuid).ToList();
        var elsewhere = (await _db.MtgjsonCards.AsNoTracking()
                .Where(c => c.SetCode != code && incomingIds.Contains(c.Uuid))
                .Select(c => c.Uuid)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        foreach (var dto in incoming.Where(c => !elsewhere.Contains(c.Uuid)))
        {
            if (!existing.Remove(dto.Uuid, out var card))
            {
                card = new MtgjsonCard { Uuid = dto.Uuid, SetCode = code };
                _db.MtgjsonCards.Add(card);
            }

            card.Name = Truncate(dto.Name, 300)!;
            card.Number = Truncate(dto.Number, 20);
            card.Rarity = Truncate(dto.Rarity, 20);
            card.CardmarketId = dto.Identifiers != null && dto.Identifiers.TryGetValue("mcmId", out var mcm) && int.TryParse(mcm, out var mcmId) ? mcmId : null;
            card.ScryfallId = Truncate(dto.Identifiers?.GetValueOrDefault("scryfallId"), 50);
            result.Cards++;
        }

        _db.MtgjsonCards.RemoveRange(existing.Values);

        if (includeBoostersAndDecks)
        {
            _db.BoosterConfigs.RemoveRange(await _db.BoosterConfigs.Include(c => c.Slots).Where(c => c.SetCode == code).ToListAsync(cancellationToken));
            _db.BoosterSheets.RemoveRange(await _db.BoosterSheets.Include(s => s.Cards).Where(s => s.SetCode == code).ToListAsync(cancellationToken));
            _db.MtgjsonDecks.RemoveRange(await _db.MtgjsonDecks.Include(d => d.Cards).Where(d => d.SetCode == code).ToListAsync(cancellationToken));

            foreach (var (boosterType, booster) in detail.Booster ?? new())
            {
                result.BoosterTypes++;
                foreach (var config in booster.Boosters)
                {
                    _db.BoosterConfigs.Add(new BoosterConfig
                    {
                        SetCode = code,
                        BoosterType = boosterType,
                        Weight = config.Weight,
                        TotalWeight = booster.BoostersTotalWeight,
                        Slots = config.Contents.Select(kv => new BoosterConfigSlot { SheetName = kv.Key, Count = kv.Value }).ToList()
                    });
                }

                foreach (var (sheetName, sheet) in booster.Sheets)
                {
                    _db.BoosterSheets.Add(new BoosterSheet
                    {
                        SetCode = code,
                        BoosterType = boosterType,
                        Name = sheetName,
                        IsFoil = sheet.Foil,
                        TotalWeight = sheet.TotalWeight,
                        Cards = sheet.Cards.Select(kv => new BoosterSheetCard { CardUuid = kv.Key, Weight = kv.Value }).ToList()
                    });
                }
            }

            foreach (var deck in detail.Decks ?? new())
            {
                var cards = (deck.Commander ?? new()).Concat(deck.MainBoard ?? new()).Concat(deck.SideBoard ?? new())
                    .Where(c => c.Uuid != Guid.Empty)
                    .Select(c => new MtgjsonDeckCard { CardUuid = c.Uuid, Count = Math.Max(1, c.Count), IsFoil = c.IsFoil })
                    .ToList();
                _db.MtgjsonDecks.Add(new MtgjsonDeck { SetCode = code, Name = Truncate(deck.Name, 300)!, Cards = cards });
                result.Decks++;
            }

            var set = await _db.MtgjsonSets.FirstOrDefaultAsync(s => s.Code == code, cancellationToken);
            if (set != null)
            {
                set.DetailImportedAt = DateTime.UtcNow;
                set.HasBoosterData = detail.Booster is { Count: > 0 };
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        // Le righe salvate non servono più: tenerle tracciate, importando decine di uscite di fila,
        // accumulerebbe centinaia di migliaia di oggetti e rallenterebbe ogni salvataggio successivo.
        _db.ChangeTracker.Clear();
        result.Sets++;
    }

    private static string? Truncate(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];
}

public class SetDetailImportResult
{
    public int Sets { get; set; }
    public int Cards { get; set; }
    public int BoosterTypes { get; set; }
    public int Decks { get; set; }
}
