using eCommerce.Inventory.Domain.Entities;

namespace eCommerce.Inventory.Application.Interfaces;

/// <summary>
/// Tiene una sola esecuzione dell'autopricer per volta e la porta avanti fuori dal ciclo
/// di richiesta HTTP.
///
/// Il vincolo di unicità non è prudenza generica: il limite in uscita verso Card Trader è
/// di 20 richieste al minuto ed è condiviso da tutta l'applicazione. Due esecuzioni in
/// parallelo non vanno il doppio più veloci, si dimezzano a vicenda e rallentano anche le
/// sincronizzazioni. Per lo stesso motivo ci passa anche l'esecuzione notturna: se una
/// manuale è ancora in corso alle 03:30, la notturna deve saperlo invece di sovrapporsi.
///
/// Lo stato è in memoria di proposito, ma il progresso no: <see cref="PricingRunLog"/>
/// viene scritto a ogni blueprint valutato, quindi l'avanzamento si legge da database e
/// sopravvive a un ricaricamento della pagina.
/// </summary>
public interface IPricingRunCoordinator
{
    /// <summary>
    /// Avvia un'esecuzione in background, se non ce n'è già una in corso.
    /// Ritorna subito: il lavoro vero prosegue dopo che il chiamante ha risposto.
    /// </summary>
    PricingRunStartResult Start(PricingRunStartRequest request);

    /// <summary>Esecuzione attualmente in corso, oppure <c>null</c> se l'autopricer è fermo.</summary>
    PricingRunStatus? Current { get; }

    /// <summary>
    /// Chiede l'interruzione dell'esecuzione in corso. L'autopricer controlla la richiesta
    /// fra un blueprint e il successivo, quindi l'arresto non è istantaneo: la carta in
    /// corso di valutazione viene comunque portata a termine.
    /// </summary>
    /// <returns><c>false</c> se non c'era nulla da interrompere.</returns>
    bool RequestCancellation();
}

/// <param name="Trigger">Origine registrata nello storico delle esecuzioni.</param>
/// <param name="Description">Testo leggibile mostrato a chi guarda l'avanzamento.</param>
/// <param name="ProfileId">Profilo da usare; se assente si prende quello attivo.</param>
/// <param name="BlueprintIds">Carte da valutare; se assente le sceglie il criterio a rotazione.</param>
/// <param name="HighValueThreshold">Sopra questo prezzo la carta rientra sempre nella selezione.</param>
/// <param name="BulkSliceSize">Quante carte bulk aggiungere alla selezione.</param>
/// <param name="ForceApply">
/// Scrive i prezzi anche con il profilo in dry-run. Riservato all'applicazione dall'anteprima,
/// dove le carte sono state guardate una per una: è il modo di uscire dalla simulazione
/// gradualmente, senza attivare la scrittura anche sull'esecuzione notturna.
/// </param>
/// <param name="BypassGuardrail">
/// Ignora il limite di variazione massima per le carte di questa esecuzione. Riservato ad
/// «Applica comunque» dalla scheda Storico, su carte già viste bloccate: un gesto esplicito
/// e circoscritto a quelle carte, non un cambio del guardrail per l'esecuzione notturna.
/// </param>
public record PricingRunStartRequest(
    PricingTrigger Trigger,
    string Description,
    int? ProfileId = null,
    IReadOnlyList<int>? BlueprintIds = null,
    decimal HighValueThreshold = 1.00m,
    int BulkSliceSize = 0,
    bool ForceApply = false,
    bool BypassGuardrail = false);

/// <summary>
/// Stato di un'esecuzione in corso, per la parte che vive in memoria.
/// I contatori di avanzamento non stanno qui: si leggono da <see cref="PricingRunLog"/>
/// tramite <see cref="RunId"/>, che però è valorizzato solo dopo la fase di preparazione.
/// </summary>
/// <param name="RunId">Riga di storico dell'esecuzione, assente finché non è stata creata.</param>
/// <param name="Phase">A che punto è, per distinguere una preparazione lenta da uno stallo.</param>
public record PricingRunStatus(
    int? RunId,
    PricingTrigger Trigger,
    string Description,
    DateTime StartedAt,
    string Phase,
    bool CancellationRequested);

/// <param name="Started">Falso se un'altra esecuzione era già in corso.</param>
/// <param name="Status">L'esecuzione avviata, oppure quella che ha impedito l'avvio.</param>
/// <param name="Completion">Si completa a esecuzione finita. Serve a chi deve attenderla.</param>
public record PricingRunStartResult(bool Started, PricingRunStatus Status, Task Completion);
