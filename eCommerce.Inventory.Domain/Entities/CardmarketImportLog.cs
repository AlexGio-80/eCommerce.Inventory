namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Esito di un import del listino Cardmarket. Un giorno saltato è storico perso, quindi l'esito
/// deve stare a registro e non solo nei log di testo.
/// </summary>
public class CardmarketImportLog
{
    public int Id { get; set; }

    public CardmarketImportTrigger Trigger { get; set; }
    public CardmarketImportOutcome Outcome { get; set; }

    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    /// <summary>Data di generazione del listino importato (<c>createdAt</c> del file).</summary>
    public DateTimeOffset? SourceCreatedAt { get; set; }

    /// <summary>
    /// <c>Last-Modified</c> del listino: se non è cambiato dall'ultimo import riuscito il file è lo
    /// stesso, e si evita di riscaricare 26 MB.
    /// </summary>
    public DateTimeOffset? SourceLastModified { get; set; }

    public int SealedProducts { get; set; }
    public int SealedSnapshotsWritten { get; set; }
    public int SinglesTracked { get; set; }
    public int SinglesSnapshotsWritten { get; set; }
    public int NewProducts { get; set; }

    public string? Message { get; set; }
}

public enum CardmarketImportTrigger
{
    Scheduled = 0,
    Startup = 1,
    Manual = 2
}

public enum CardmarketImportOutcome
{
    Running = 0,
    Succeeded = 1,
    /// <summary>Listino già importato: nessuna riga scritta, non è un errore.</summary>
    Skipped = 2,
    Failed = 3
}
