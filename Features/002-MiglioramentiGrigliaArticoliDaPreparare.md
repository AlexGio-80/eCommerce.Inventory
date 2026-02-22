## [2026-02-22] Miglioramento calcolo prezzi da proporre in inserimento carte
Leggi ..\Documentation\ARCHITECTURE.md per sapere le linee guida del progetto e come gestire le implementazioni

### Problema
Nella maschera di Items to Prepare, abbiamo implementato la griglia degli articoli da preparare per la spedizione su cui voglio fare alcuni miglioramenti.
1) Attualmente abbiamo un sistema per visualizzare più in grande l'immagine della carta di una data riga. questa funzionalità prevede di essere posizionati precisamente sull'immagine presente nella riga della griglia; Voglio fare in modo che l'anteprima ingrandita venga mostrata in qualunque punto della riga io sia posizionato.
2) Attualmente per "marcare" in una riga la carta come preparata (eCommerceInventory.dbo.OrderItems.IsPrepared = 1), devo cliccare su una check box che però è piccola e poco comoda. Vorrei quindi sostituire l'attuale check box con un pulsante più grande e facile da cliccare.
3) Questa è la soluzione più complessa: attulamente la griglia mi permette di ordinare i dati in base alle colonne visualizzate. Questo ordinamento ha però un difetto e cioè non mi permette di ordinare le espansioni per data di uscita. Da quello che ho visto (ma fai un'analisi anche tu con le Api di Card Trader), la data di uscita, o un'ordinamento temporale relativo alle espansioni non è un dato fornito. se così non fosse, si potrebbe basarsi sui dati presi da un altro sito dove le espansioni di Magic: the Gathering sono perfettamente ordinate per data di uscita, in modo da avere questo dato e poterlo poi usare per ordinare i dati nella griglia. Il sito in questione è https://scryfall.com/sets. In questa pagina troviamo tutte le espansioni uscite dal 1993, con una icona o bitmap del simbolo dell'espansione (anche questo non mi dispiacerebbe avere a disposizione nelle espansioni e poi poterlo riportare nella griglia degli Items to prepare, ma è una cosa secondaria), e soprattutto c'è la data di release che è poi il dato che a noi interessa. io la pagina la vedo come una griglia con icons, nome dell'espansione in inglese, codice dell'espansione (che potresti usare per provare a fare il match con i nostri dati nella tabella eCommerceInventory.dbo.Expansions), il numero di carte dell'espansione, la data di release (dato che ci interessa maggiormente) e le lingue in cui è uscita l'espansione. vediamo se è possibile prendere i dati da questo sito perché sarebbe poi una cosa che possiamo automatizzare quando viene fatta la sincronizzazione delle espansioni in quanto questo sito viene sempre aggiornato con le nuove uscite e quindi è molto attendibile.

### Causa Radice
Miglioramento form per la preparazione degli oggetti da spedire.

### Soluzione Implementata
Ho implementato una soluzione completa che integra dati da Card Trader e Scryfall per migliorare l'esperienza utente nella preparazione degli ordini.

#### 1) Anteprima Immagine Row-Level
- Modificato `unprepared-items.component.ts` per gestire l'evento `rowMouseOver`.
- L'anteprima della carta viene ora mostrata indipendentemente da quale cella della riga venga sorvolata, migliorando la velocità di identificazione delle carte.

#### 2) Pulsante "Prepare"
- Sostituita la checkbox con un pulsante verde personalizzato ("Prepare") nella colonna delle azioni.
- Il pulsante è molto più grande e facile da cliccare rispetto alla checkbox originale.

#### 3) Integrazione Scryfall (Date e Icone)
- **Backend**: Creato `ScryfallApiClient` per interrogare `api.scryfall.com/sets`.
- **Sincronizzazione**: `CardTraderSyncOrchestrator` ora scarica i set da Scryfall e abbina (tramite codice espansione) la data di uscita e l'icona SVG alle espansioni di Card Trader.
- **Database**: Aggiunte colonne `ReleaseDate` e `IconSvgUri` alla tabella `Expansions`.
- **UI**: 
  - Aggiunta colonna "Icon" con rendering SVG sia in "Items to Prepare" che in "Expansions".
  - Aggiunta colonna "Rel. Date" visibile.
  - Implementato ordinamento predefinito: **Data Uscita (Crescente)** e poi **Collector Number (Crescente)**.

### Verifica
- **Sincronizzazione**: Verificato che i log di sistema riportino il corretto abbinamento dei set tra le due API.
- **Griglia**: Confermato che le icone SVG vengano renderizzate correttamente e che l'ordinamento multi-colonna funzioni come previsto.
- **Build**: Codice backend compilato senza errori.

### Note Tecniche
- Il matching tra Scryfall e Card Trader avviene tramite il codice dell'espansione (es. "BNG", "MH3").
- È stata aggiunta una gestione per i casi in cui Scryfall non restituisca un'icona (placeholder "No Icon" aggiunto per debug).
- L'ordinamento della griglia è stato forzato via codice Angular per garantire che all'apertura i dati siano già organizzati per data di uscita.
