# eCommerce.Inventory - Changelog

> Una voce per ogni sessione di lavoro significativa. Le voci più recenti vanno in cima.
> Formato sezioni: **Problema** (cosa non funzionava), **Soluzione** (cosa è cambiato), **Note Tecniche** (dettagli per manutenzione futura).

---

## [Unreleased]

> Modifiche in corso, non ancora in produzione.

### [2026-08-29] Feature — Storico dei prezzi alimentato dalla sincronizzazione

#### Problema

Per ricostruire l'andamento del prezzo di una carta nel tempo esisteva solo `PriceChangeLogs`, che registra le valutazioni dell'autopricer. Sono dati utili — portano anche il riferimento di mercato — ma con tre limiti per un grafico: la copertura è a rotazione, quindi la serie è rada (su 4.877 carte toccate in due notti, 2.288 avevano un solo punto e 1.562 due); vede solo ciò che fa l'autopricer, quindi un cambio manuale o dell'autopricer nativo di Card Trader compare solo come salto di `OldPrice` alla valutazione successiva; e `OldPrice` è il prezzo venditore mentre `ReferencePrice` è un prezzo di vetrina, quindi le due colonne non sono confrontabili sullo stesso asse.

#### Soluzione Implementata

Nuova tabella `PriceHistoryEntries`, alimentata dalla sincronizzazione notturna. La scelta del punto di aggancio è il motivo per cui costa poco: la sync **scarica già l'export completo con tutti i prezzi**, quindi la rilevazione non aggiunge una sola chiamata alle API, che sono la risorsa scarsa. Ne risulta una serie regolare su tutte le inserzioni, indipendente dalla copertura dell'autopricer, che cattura anche i cambi fatti a mano.

**Serie a delta.** Una riga esiste solo quando prezzo o quantità cambiano rispetto alla rilevazione precedente. Scrivere ogni notte tutte le 35.000 inserzioni produrrebbe circa 12 milioni di righe l'anno per rappresentare in gran parte prezzi fermi; per ricostruire un andamento basta sapere quando è cambiato. Fa eccezione il primo punto di ogni inserzione, che va scritto anche se nulla è cambiato: senza, una carta dal prezzo stabile non comparirebbe affatto e sarebbe indistinguibile da una di cui non si sa nulla.

**La quantità viene registrata accanto al prezzo.** Messa in grafico mostra quando una carta è stata comprata, che è il contesto che spiega i movimenti di prezzo — proprio lo scenario delle copie multiple spazzate via da un rialzo improvviso.

`InventoryItemId` è nullable con `SET NULL` e le caratteristiche della versione (condizione, lingua, foil) sono denormalizzate sulla riga: la serie deve sopravvivere alla vendita dell'inserzione, altrimenti si perderebbe l'andamento proprio delle carte vendute, e una serie per blueprint mescolerebbe la foil con la non foil.

#### Verifica

Su una copia ripristinata dal backup delle 03:02, con la sincronizzazione dell'inventario eseguita tre volte:

| esecuzione | rilevazioni scritte | su osservate |
|---|---|---|
| prima (storico vuoto) | 35.219 | 35.219 |
| seconda (nulla cambiato) | 0 | 35.219 |
| terza (3 prezzi alterati) | 3 | 35.219 |

Il primo giro stabilisce la linea di partenza, il secondo dimostra che i prezzi fermi non occupano spazio, il terzo che un cambiamento viene colto con precisione.

#### Note Tecniche

- La decisione su cosa registrare sta in `PriceHistoryRecorder`, funzione pura senza database né rete, coperta da sei test.
- La scrittura è dentro un `try/catch` che al massimo logga: lo storico è un'osservazione a margine e non deve poter far fallire l'allineamento dell'inventario, che è il compito vero della sincronizzazione.
- Lo stato precedente va catturato **prima** che il mapper aggiorni le entità in memoria: dopo, il valore vecchio non è più recuperabile.
- Resta aperto il disallineamento di scala su `PriceChangeLogs`, dove `ReferencePrice` è in termini di vetrina e `OldPrice` in termini di venditore. Non riguarda questa tabella, che usa una scala sola.

---

### [2026-08-29] Fix — Le migration si applicano all'avvio in ogni ambiente

#### Problema

`MigrateAsync()` era dentro un `if (app.Environment.IsDevelopment())`: in produzione lo schema andava aggiornato a mano, e non era scritto da nessuna parte con sufficiente evidenza. Due deploy consecutivi sono partiti con i binari nuovi contro uno schema vecchio, e l'autopricer falliva a ogni lettura del profilo — senza lasciare traccia, perché nella stessa finestra i log non funzionavano.

#### Soluzione Implementata

Il blocco è uscito dalla guardia sull'ambiente. Prima di applicare, l'elenco delle migration pendenti viene registrato a log: resta così traccia di quando lo schema è cambiato e di cosa è cambiato, cosa che con l'applicazione manuale non esisteva. Se la migrazione fallisce l'applicazione non parte, ed è la scelta voluta: un servizio fermo si nota, uno che scrive su uno schema che non corrisponde al modello no.

**Nessun interruttore per disattivarle.** Un flag lasciato a `false` riprodurrebbe esattamente lo stesso disallineamento, stavolta senza nemmeno un passaggio dimenticato da ricostruire.

Separato di conseguenza il seed: il profilo di pricing predefinito viene creato in ogni ambiente, perché senza l'autopricer non ha regole; i dati dimostrativi (giochi, espansioni e blueprint fittizi) restano confinati allo sviluppo, dove prima erano di fatto protetti solo dal fatto che in produzione la tabella `Games` non è mai vuota.

#### Note Tecniche

- Verificato su una copia del database con una migration pendente: il log riporta `Migration da applicare (1): 20260829041648_PercentileEGuardrailAsimmetrico`, la applica, e al riavvio successivo dichiara `Nessuna migration da applicare`.
- Il backup prima di un deploy con migration resta consigliato: l'automatismo elimina il rischio di dimenticarsene, non quello di una migration sbagliata.

---

### [2026-08-29] Fix — L'autopricer confrontava prezzi su scale diverse e cadeva sui prezzi di comodo

#### Problema

Verifiche a campione col simulatore hanno mostrato proposte incomprensibili: Overgrown Tomb a 19,99 € riceveva una proposta di rialzo pur essendo già in terza posizione fra offerte comparabili, e Sigarda's Aid, su un mercato di 73–96 €, veniva valutata **1019,61 €**.

**Causa 1 — due grandezze diverse messe a confronto.** `InventoryItem.ListingPrice` è il prezzo che incassa il venditore, quello dell'export. Le offerte del marketplace sono invece prezzi lato acquirente, comprensivi del sovrapprezzo di Card Trader: la nostra stessa inserzione compare nel feed a un valore più alto di quello impostato. Misurato prendendo i due endpoint nello stesso istante:

| carta | export (venditore) | marketplace (acquirente) |
|---|---|---|
| Overgrown Tomb foil | 19,99 € | 20,26 € |
| The Ozolith | 63,07 € | 63,62 € |
| Propaganda | 61,98 € | 62,62 € |

Confrontando 19,99 € con i 20,2x € dei concorrenti il motore si credeva più economico di quanto fosse e proponeva un rialzo. Il sovrapprezzo non è una percentuale fissa (osservato fra 0,76% e 1,35%).

**Causa 2 — l'ordinale fisso degenera sui mercati sottili.** Le offerte comparabili misurate sulle carte reali vanno da 3 a 29. Con "posizione 4" fissa, in **4 casi su 11** il riferimento cadeva esattamente sull'offerta più cara: la regola non posizionava più, diceva "sii il più caro".

**Causa 3 — lo scarto degli outlier non girava dove serviva.** `MinOffersForOutlierRejection` era 5. Sigarda's Aid aveva 4 offerte comparabili, quindi il filtro non partiva affatto e il prezzo di comodo da 1019 € arrivava intatto al riferimento. Con 5 offerte lo avrebbe scartato senza esitazione: quel valore sta a 55 deviazioni dalla mediana contro una soglia di 3.

#### Soluzione Implementata

**Conversione fra le due scale.** Il motore individua la propria inserzione nel feed tramite il `CardTraderProductId` e ne ricava il rapporto fra prezzo esposto e prezzo incassato. Non serve conoscere la formula della commissione: il fattore è esatto per definizione e si aggiorna da solo. Le regole ragionano sulla posizione in vetrina, il risultato viene riportato al prezzo venditore prima di essere scritto. Se il rapporto risulta implausibile — prezzo esposto inferiore a quello incassato, tipico quando il prezzo è appena stato cambiato a mano — non si converte e la motivazione lo dichiara.

**Collocazione percentuale al posto della posizione fissa.** Nuova modalità `PercentileOffer`: "collocati al N% della scaletta" invece di "la N-esima più bassa". Si adatta da sola alla profondità del mercato. Le regole esistenti sono state convertite: 15% sul bulk, 20% nella fascia 1–25 €, 40% sopra i 25 €.

**Il riferimento non può mai essere l'offerta più cara**, in nessuna modalità.

**Due difese contro i prezzi anomali, complementari.** Un filtro di rapporto sulla mediana (oltre 4× o sotto un quarto) che gira **a qualunque numero di offerte**, più lo scarto statistico con MAD la cui soglia scende da 5 a 3 offerte. Il primo è grossolano ma funziona dove la statistica non arriva; intercetta le due patologie descritte dall'utente: i prezzi messi altissimi per non sbagliare e quelli irrealistici dei venditori alle prime armi.

**Guardrail asimmetrico.** `MaxChangePercentPerRun` si sdoppia in `MaxIncreasePercentPerRun` (300%) e `MaxDecreasePercentPerRun` (25%). Le due direzioni non hanno lo stesso costo se sbagliate: un rialzo eccessivo lascia la carta invenduta e si corregge all'esecuzione successiva, un ribasso eccessivo la fa comprare subito al prezzo sbagliato e non si recupera. La difesa dai prezzi anomali non è più affidata a questa soglia, quindi può essere generosa in salita — che è la ragione principale per cui l'autopricer esiste.

**Motivazioni leggibili.** Ogni riga ora ricostruisce il percorso: posizione attuale in vetrina, sovrapprezzo, offerta presa a riferimento, scostamenti, riconversione.

#### Verifica

Effetto misurato sulle carte reali, con export e marketplace letti in diretta:

```
carta                  mio      PRIMA       ADESSO      esito
Sigarda's Aid          75.72    1019.61  →    78.29     applicata (+3%)
Sonic Screwdriver      60.56      75.18  →    57.69     applicata (-5%)
Mystic Remora          62.14      79.36  →    78.69     applicata (+27%)
The Ozolith            63.07      84.03  →    85.93     applicata (+37%)
Mountain               68.46      87.18  →    27.15     BLOCCATA (-60%)
```

I rialzi corroborati da un gruppo di venditori concordi restano; il caso da 1019 € sparisce; su Mountain, mercato di 4 offerte sparse fra 27 e 87 €, il guardrail in discesa ferma la proposta.

#### Note Tecniche

- Analisi di sensibilità sul percentile eseguita su 11 carte reali: le carte davvero sottoprezzo danno lo stesso risultato dal 20% al 60%, segno che il segnale è robusto e non un artefatto della taratura. Le percentuali restano da affinare guardando l'anteprima.
- Sui mercati profondi il percentile è **più aggressivo** dell'ordinale precedente (su Overgrown Tomb si passa dalla terza alla quinta posizione circa): è un cambio di postura voluto, non un effetto collaterale.
- La migration `PercentileEGuardrailAsimmetrico` è stata scritta a mano: quella generata da EF rinominava `MaxChangePercentPerRun` in `MaxMedianRatio`, portandosi dietro il valore 50 (che come rapporto disattiva il filtro) e lasciando i due guardrail a zero, cioè senza limite.

---

### [2026-08-29] Feature — La vendita scala subito la giacenza e non spreca rivalutazioni

#### Problema

La quantità a magazzino veniva scritta solo dall'export durante la sincronizzazione notturna: nessuno la scalava alla vendita. Per tutta la giornata l'inventario mostrava quindi carte già vendute. Il controllo "riprezza solo se resta qualcosa", presente nel webhook, leggeva quel dato vecchio e risultava sempre vero: anche vendendo l'ultima copia si spendeva una chiamata al marketplace — risorsa limitata a 20 al minuto — per riprezzare una carta che non c'era più. A dry-run spento si sarebbe arrivati a scrivere un prezzo su un'inserzione inesistente.

#### Soluzione Implementata

All'arrivo del webhook `order.create` la giacenza locale viene scalata delle quantità vendute, confrontate per `product_id` e non per blueprint, così regge anche il caso di due inserzioni della stessa carta in condizioni diverse.

Due protezioni:
- **Idempotenza**: l'esistenza dell'ordine viene verificata *prima* della sincronizzazione, che fa insert-o-update e dopo renderebbe indistinguibile un ordine già visto. Card Trader può recapitare lo stesso webhook più volte, e un doppio scarico farebbe sparire dall'inventario una carta con una sola copia.
- **Mai sotto zero**: se le copie vendute superano quelle registrate, il dato era già disallineato e portarlo in negativo aggiungerebbe un secondo errore.

Il rischio complessivo resta basso perché la verità è l'export, che la sincronizzazione riscrive in valore assoluto: un errore qui si riassorbe la notte successiva e non si accumula. Per lo stesso motivo l'operazione non può far fallire la registrazione dell'ordine.

Il controllo a valle è stato semplificato per leggere la giacenza già aggiornata: sottrarre di nuovo le quantità dell'ordine le avrebbe contate due volte, saltando rivalutazioni ancora dovute.

---

### [2026-08-29] Fix — I log di produzione finivano in C:\Windows\System32

#### Problema

Dopo la correzione del livello di log del 28/08 la cartella `Publish/api/logs` restava comunque vuota, anche con i permessi corretti.

La causa è stata individuata grazie al `SelfLog` di Serilog aggiunto nella stessa sessione: **un servizio Windows eredita come cartella corrente `C:\Windows\System32`**, non quella dell'eseguibile. Il percorso relativo `logs/ecommerce-inventory-.txt` veniva quindi risolto lì dentro, dove l'account del servizio non ha permesso di scrittura. È anche la spiegazione del file `taffel-inventory-20251126.txt` trovato in `System32\logs`. L'assunzione che `UseWindowsService()` riallineasse la working directory era sbagliata.

Un secondo difetto, sempre segnalato dal SelfLog: gli enricher `WithThreadId` e `WithProcessId` richiedono pacchetti non referenziati, e Serilog li ignorava.

#### Soluzione Implementata

`Program.cs` allinea la cartella corrente a `AppContext.BaseDirectory` quando il processo gira come servizio, prima di costruire l'host. Corregge tutti i percorsi relativi, non solo quello dei log: anche `Backup:BackupPath` ne beneficia. Rimossi dalla configurazione gli enricher non disponibili.

Corretto inoltre `publish.ps1`, che concedeva i permessi con `icacls` usando il nome `NT AUTHORITY\NETWORK SERVICE`: su Windows italiano non si risolve, e l'errore era ingoiato tre volte (`2>$null`, `| Out-Null`, e un `catch` che non scatta mai perché `icacls` segnala con l'exit code). Ora usa il SID `*S-1-5-20` e verifica `$LASTEXITCODE`.

---

### [2026-08-28] Fix — La sincronizzazione dell'inventario era ferma da dicembre 2025 senza segnalarlo

#### Problema

Una carta venduta la settimana precedente (Galadriel's Dismissal) risultava ancora a magazzino nell'anteprima dell'autopricer, pur non essendo più su Card Trader. Il confronto fra il database e l'export di Card Trader ha misurato la deriva reale:

| | |
|---|---|
| Articoli nel DB non più su Card Trader | 282 |
| Carte Magic su Card Trader assenti dal DB | 192 |
| Quantità disallineate | 203 |

Nessun log lo segnalava, e le metriche riportavano l'esecuzione notturna come riuscita.

**Causa** — in `CardTraderSyncOrchestrator.UpsertInventoryAsync` il lookup di `Tag` e `PurchasePrice` da `PendingListings` costruiva un dizionario con `ToDictionaryAsync`, che solleva un'eccezione sulla chiave duplicata. Lo stesso `CardTraderProductId` compare su più `PendingListings` (550 casi, il primo del 03/12/2025: ripubblicazioni e riallineamenti manuali). L'eccezione arrivava **prima** del ciclo di upsert, quindi ogni notte la sezione inventario abortiva senza inserire né cancellare nulla.

**Perché non si notava** — due meccanismi indipendenti la nascondevano:

1. `SyncInventoryAsync` impostava `response.Inventory.ErrorMessage` ma **non** `response.ErrorMessage`, e `ScheduledProductSyncWorker` valuta l'esito solo su quest'ultimo. Una sezione poteva fallire in blocco e l'esecuzione risultava comunque `success`, sia nel log riepilogativo sia nella metrica `ecommerce_sync_total`.
2. In produzione Serilog era a `MinimumLevel: Warning`, e il sink su file non crea nemmeno il file finché non si verifica un evento di quel livello: la cartella `Publish/api/logs` risultava vuota e i riepiloghi di sync e autopricer (che sono `Information`) non lasciavano traccia.

Un terzo elemento ha reso il sintomo ancora meno visibile: i prezzi nel DB coincidevano al 100% con Card Trader in ogni fascia, il che sembrava provare che la sync funzionasse. In realtà sono i prezzi di vendita dell'utente, non quotazioni di mercato: con l'autopricer in dry-run nessuno li cambia, quindi le due parti restavano identiche anche senza sync. E i nuovi articoli comparivano lo stesso perché il flusso `PendingListings` crea gli `InventoryItem` direttamente quando si pubblica dall'app — solo le carte messe in vendita dal sito di Card Trader mancavano.

#### Soluzione Implementata

**Il difetto** — il lookup raggruppa per `CardTraderProductId` e tiene la registrazione con `CreatedAt` più recente, cioè quella che riflette l'ultima messa in vendita. Stessa correzione applicata a `InventorySyncService.SyncProductsAsync`, che ha la medesima struttura (oggi non attiva: il suo worker `CardTraderSyncWorker` è commentato in `Program.cs`).

**Il reporting** — `SyncAsync` raccoglie a fine esecuzione le sezioni con `ErrorMessage` valorizzato e le propaga su `response.ErrorMessage`, con un log a livello `Error` che le elenca. Da qui in avanti un fallimento parziale marca l'esecuzione come fallita anche nella metrica Prometheus.

**I log** — in produzione `MinimumLevel` passa da `Warning` a `Information` con `Override` su `Microsoft`, `Microsoft.EntityFrameworkCore` e `System`, così i riepiloghi si vedono senza il rumore di EF. Aggiunto `retainedFileCountLimit: 14`. Rimosso inoltre il sink File da `appsettings.json`: gli array di configurazione in .NET si fondono **per indice** e non si concatenano, quindi un sink dichiarato nella base più uno dichiarato nel file per ambiente producevano due sink sullo stesso percorso, di cui il secondo non riusciva a prendere il lock — è l'origine dei file con suffisso `_001`.

**Lo storico dei prezzi** — `FK_PriceChangeLogs_InventoryItems_InventoryItemId` era in `CASCADE`: alla prima sync corretta la cancellazione delle carte vendute si sarebbe portata via anche il loro storico di valutazioni, cioè proprio le carte su cui conviene verificare se il prezzo proposto era corretto. Misurato: 83 righe su 4.799 dell'esecuzione del 28/08. La foreign key passa a `SET NULL` con `PriceChangeLog.InventoryItemId` nullable (migration `20260828071742_PreservaStoricoPrezziCarteVendute`). La carta resta identificabile da `BlueprintId`, e `InventoryItemId IS NULL` diventa il modo per interrogare le valutazioni di carte non più a magazzino.

#### Verifica

Ripristinato il backup delle 03:00 come database separato `eCommerceInventory_Diag` ed eseguita lì la sincronizzazione, senza toccare la produzione:

```
added: 192   updated: 35.045   skipped: 29   failed: 0   errori: nessuno
35.327 - 282 + 192 = 35.237 articoli
```

35.237 corrisponde esattamente alle carte Magic presenti su Card Trader (i 29 saltati sono Pokémon, gioco disabilitato). Galadriel's Dismissal non risulta più a magazzino.

Applicata poi la migration alla stessa copia e cancellata una carta con storico: 4.719 righe di registro prima, 4.719 dopo, con le righe della carta cancellata conservate e ancora identificabili.

#### Note Tecniche

- Tre test di regressione: `SyncProductsAsync_ShouldNotThrow_WhenSameProductHasDuplicatePendingListings` e i due in `PriceChangeLogDeleteBehaviorTests` (uno sui metadati del modello, uno sul comportamento reale di cancellazione). Verificati rimettendo temporaneamente `CASCADE`: falliscono entrambi.
- `POST /api/cardtrader/sync/products` e `POST /api/cardtrader/sync/orders` **non scrivono nulla a database**: recuperano i dati da Card Trader e restituiscono solo un conteggio. Per una sincronizzazione reale serve `POST /api/cardtrader/sync` con i flag della sezione desiderata.
- `GET /api/pricing/runs/{id}/changes` accetta ora un parametro `outcome` per filtrare per esito lato server e restituisce `{ totalCount, returnedCount, items }`. Serviva perché su un'esecuzione notturna le righe sono migliaia e il tetto sul numero restituito mostrava solo le variazioni di importo maggiore, lasciando invisibili le 3.604 bloccate dal guardrail.

---

### [2026-08-28] Feature — Dettaglio carta per carta delle esecuzioni dell'autopricer

#### Problema

Le schede Copertura e Storico mostravano solo i riepiloghi delle esecuzioni. Non c'era modo di vedere i calcoli e i prezzi proposti, quindi non si potevano fare le verifiche a campione necessarie a decidere se uscire dal dry-run. I dati erano già tutti a database in `PriceChangeLogs`, l'endpoint `GET /api/pricing/runs/{id}/changes` esisteva e il metodo `getRunChanges()` era già nel servizio Angular: mancava solo il pezzo di interfaccia che li collegasse.

#### Soluzione Implementata

Nella scheda Storico la riga di un'esecuzione è ora cliccabile e apre il dettaglio: griglia con carta, prezzo attuale, proposto, variazione, offerte comparabili, anomale scartate, esito e motivazione testuale. Filtro per esito lato server, per isolare per esempio le sole bloccate dal guardrail.

Aggiunta la colonna **Magazzino**: dopo il passaggio della foreign key a `SET NULL` una valutazione riferita a una carta ormai venduta sarebbe stata indistinguibile da una ancora a magazzino, e lo storico conservato sarebbe rimasto invisibile.

#### Note Tecniche

- `PRICING_OUTCOMES` in `pricing.service.ts` tiene le etichette allineate all'enum `PricingOutcome` del backend.
- L'endpoint valida il parametro `outcome` contro l'enum e risponde `400` con l'elenco dei valori ammessi.

---

### [2026-08-27] Feature — Autopricer custom (motore a regole, esecuzione notturna, reprice alla vendita)

#### Problema
L'autopricer nativo di Card Trader ha tre limiti: non aggiorna tutte le carte, non permette di scegliere il campione di venditori su cui tarare il prezzo, e agisce solo una volta al giorno anche quando una vendita segnala che il mercato si sta muovendo.

#### Soluzione Implementata

**Motore (`Application/Pricing/PricingEngine.cs`)** — logica pura, senza dipendenze da rete o database, coperta da 21 test. Passi in ordine: esclusione delle proprie offerte, comparabilità (condizione/lingua/foil con normalizzazione `en`↔`English`), filtri venditore, scarto outlier, scelta regola per fascia, controllo profondità di mercato, prezzo minimo, direzione consentita, guardrail sulla variazione massima.

**Filtro venditori** — l'API Card Trader **non espone il numero di recensioni**: verificato su offerte reali, l'oggetto `user` contiene solo `id`, `username`, `user_type` (`pro`/`normal`), `country_code`, `max_sellable_in24h_quantity`, `one_day_ready` e i flag hub. Al posto del filtro sul feedback si usa lo scarto statistico degli outlier con MAD (Median Absolute Deviation, preferita alla deviazione standard perché non viene distorta dagli outlier stessi), affiancato ai filtri realmente disponibili.

**Copertura (`SelectBlueprintsForScheduledRunAsync`)** — con ~19.000 blueprint distinti e 20 richieste al minuto un giro completo richiederebbe 16 ore. Ogni notte si coprono per intero le carte sopra soglia e si aggiunge una fetta a rotazione del bulk, scegliendo quelle ferme da più tempo.

**Riallineamento prezzi** — prima di ogni esecuzione i prezzi locali vengono riallineati a Card Trader tramite l'endpoint di export (una sola chiamata). Alla prima esecuzione **2856 prezzi locali risultavano disallineati**.

**Reprice alla vendita** — il webhook `order.create` accoda i blueprint venduti su `IPriceRefreshQueue` e risponde subito; `OrderTriggeredPricingWorker` consuma la coda in background. Vengono accodate solo le carte ancora a magazzino.

**Dry-run** — modalità permanente del profilo, non impalcatura temporanea: si spegne quando le regole sono tarate e si riaccende per verificare una modifica.

**Interfaccia** (`/layout/pricing`) — quattro schede: Regole (con interruttore simulazione/attivo evidenziato), Anteprima, Copertura, Storico.

#### Note Tecniche
- Il flag `SkipWhenFewerOffersThanPosition` evita che una regola posizionale su mercato sottile allinei all'offerta più cara. Osservato su dati reali: con 2 venditori e "posizione 3" la carta finiva a 2000 € contro un mercato di 0,92–12,69 €, e l'altro venditore era a 2000,64 € — due autopricer che si rincorrono al rialzo.
- Le migration aggiungono colonne bool con default `false`: un profilo già esistente non eredita il default C#. Dopo `AddSkipWhenFewerOffersThanPosition` è stato necessario un `UPDATE` esplicito.
- `AutoPricing:Enabled` e `AutoPricing:RepriceOnOrder` sono `false` in dev e `true` in produzione.

---

### [2026-08-27] Fix — Ripristino wiring di produzione disattivato durante il debug del monitoring

#### Problema
Durante l'implementazione della Fase 1 Monitoring il wiring di produzione era stato commentato in `Program.cs` per test in locale e mai riattivato. Conseguenze in produzione:
- `UseWindowsService` disattivato → il servizio Windows `eCommerce.Inventory` non poteva funzionare ed era stato rimosso
- `UseUrls(apiBaseUrl)` disattivato → API non più in ascolto su porta 5152, frontend IIS senza backend
- `ScheduledProductSyncWorker` disattivato → nessuna sync notturna
- `BackupService` + `Configure<BackupSettings>` disattivati → **nessun backup giornaliero**
- `PopulateItalianNamesService` e `SealedProductPriceService` disattivati

Inoltre `/health` rispondeva **HTTP 503 dopo 15,4 secondi**: `Redis:Enabled` era `true` in `appsettings.json` ma nessun server Redis è installato sulla macchina, quindi ogni probe restava appeso sul connect TCP. Lo stesso stallo consumava il budget di timeout del check Card Trader, che risultava a sua volta `Unhealthy` con "A task was canceled".

#### Soluzione Implementata
- Riattivati tutti i blocchi commentati in `Program.cs` (Windows Service, UseUrls, 4 hosted service, BackupSettings)
- `Redis:Enabled` → `false` in `appsettings.json`, allineando la config alla realtà della macchina. Il codice di caching resta intatto: per riattivarlo basta installare Redis e rimettere il flag a `true`
- `CardTraderApiHealthCheck`: il fallimento di un'API esterna ora produce `Degraded` invece di `Unhealthy`, così `/health` resta 200 e le liveness probe non riavviano l'applicazione per un problema che non è nostro. Il timeout è distinto dagli altri errori tramite exception filter su `timeoutCts.IsCancellationRequested`
- `appsettings.Development.json`: `PopulateSealedPricesOnStartup` → `false`. Era `true`, e quel servizio termina con `Environment.Exit(0)`: in dev l'applicazione si spegneva da sola subito dopo l'avvio
- Rimossi endpoint di debug (`/test-debug`, `/test-minimal`, `/test-health`) e i `Log.Information` di tracciamento lasciati nella pipeline
- Rimossi dalla root i file spuri `Program.cs` (Hello World), `stop` e la cartella con il path preso alla lettera `C:LavoroeCommerce.Inventory...`

#### Verifica
Con applicazione effettivamente avviata:
- `/health` — da **15,4s / HTTP 503** a **0,33s / HTTP 200**, tutti e tre i check Healthy
- In produzione dopo il deploy: servizio `RUNNING`, `/health` su 5152 in **0,19s / HTTP 200**
- Login `admin` → JWT valido (HTTP 200); `/metrics` risponde
- Correlation ID verificato sia in generazione sia in propagazione (`X-Correlation-ID` echo)
- Build backend 0 errori, build frontend OK

#### Note Tecniche
I servizi one-shot `PopulateItalianNamesService` e `SealedProductPriceService` chiudono il processo con `Environment.Exit(0)`. La guard clause sul flag di configurazione precede l'`Exit`, quindi con flag `false` sono innocui — ma **non vanno mai abilitati in `appsettings.Production.json`**, pena lo spegnimento del servizio Windows a ogni avvio. Vanno lanciati on-demand dagli endpoint dedicati.

---

### [2026-08-27] Feature — Monitoring/Observability Fase 1 Core (Prometheus + OpenTelemetry + Correlation ID + Serilog Config)

#### Problema
Mancava il layer di observability completo per produzione: metriche Prometheus, distributed tracing OpenTelemetry, correlation ID propagation, e configurazione Serilog environment-specific.

#### Soluzione Implementata

**Backend — Nuovi pacchetti e configurazioni:**

**Pacchetti NuGet aggiunti:**
- `prometheus-net.AspNetCore` 8.2.1 + `prometheus-net.DotNetRuntime` 4.3.0 — endpoint `/metrics` con runtime metrics
- `OpenTelemetry.Extensions.Hosting` 1.11.1, `OpenTelemetry.Instrumentation.AspNetCore` 1.11.1, `OpenTelemetry.Instrumentation.Http` 1.11.1, `OpenTelemetry.Instrumentation.EntityFrameworkCore` 1.12.0-beta.1, `OpenTelemetry.Exporter.Console` 1.11.1 — distributed tracing
- `AspNetCore.HealthChecks.UI` 8.0.0 + `AspNetCore.HealthChecks.UI.Client` 8.0.0 + `AspNetCore.HealthChecks.UI.InMemory.Storage` 8.0.0 — Health Checks UI

**Program.cs — Major refactor:**
- Serilog: lettura configurazione da `builder.Configuration.GetSection("Serilog")` invece di hardcoded
- OpenTelemetry: `AddOpenTelemetry()` con `WithTracing` (AspNetCore, HttpClient, EF Core) + `WithMetrics` (AspNetCore, HttpClient, Prometheus exporter, Console exporter)
- Prometheus: `app.MapMetrics()` endpoint `/metrics` (minimal API)
- Correlation ID Middleware: nuovo `UseCorrelationId()` registrato PRIMA di `UseSerilogRequestLogging()`
- Health Checks: `AddHealthChecksUI()` con InMemory storage, endpoint `/health` (JSON detailed) e `/health-ui`
- Rate Limiting: 4 policies (api 100/min, cardtrader-sync 10/min, auth 5/min sliding, global 200/min)
- CORS: `AllowAll` policy per `localhost:4200`, `127.0.0.1:4200`, `inventory.local`
- Middleware order corretto: `UseRouting` → `UseRateLimiter` → `UseAuthentication` → `UseAuthorization` → `MapHealthChecks` → `MapMetrics` → `MapControllers`

**Nuovo file: `Middleware/CorrelationIdMiddleware.cs`**
- Estrae/propaga header `X-Correlation-ID` da request
- Genera nuovo ID se assente (Activity.Current?.Id o Guid)
- Enrichment Serilog LogContext: `CorrelationId`, `TraceId`, `SpanId`

**appsettings.json / appsettings.Development.json / appsettings.Production.json — Serilog config:**
- Development: `MinimumLevel: Debug`, Console + File sinks, outputTemplate con Properties JSON
- Production: `MinimumLevel: Warning`, File only, Enrich: `FromLogContext`, `WithThreadId`, `WithProcessId`

**BusinessMetrics.cs (Application/Metrics) — 20+ metriche custom:**
- Sync: `SyncDurationHistogram`, `SyncSuccessCounter`, `SyncFailureCounter`
- Orders: `OrdersCreatedCounter`
- Inventory: `InventoryItemsGauge`
- API: `ApiCallsTotal` (Counter con labels endpoint/method/status), `WebhooksReceivedTotal`
- DB: `DbQueryDurationHistogram`
- Cache: `CacheHitsTotal`, `CacheMissesTotal`
- Background Jobs: `BackgroundJobExecutionsTotal`, `BackgroundJobDurationHistogram`
- Auth: `AuthAttemptsTotal` (Counter con labels endpoint/result)
- SignalR: `ActiveUsersGauge`

**Integrazione metriche nei worker esistenti:**
- `CardTraderSyncWorker.cs`: usa `BusinessMetrics.SyncDurationHistogram.NewTimer()`, incrementa `SyncSuccessCounter`/`SyncFailureCounter`
- `ScheduledProductSyncWorker.cs`: stesso pattern con labels `syncType: "ScheduledProductSync"`

**Health Checks con timeout:**
- `RedisHealthCheck`: timeout 3s per health check (fail fast se Redis non disponibile)
- `CardTraderApiHealthCheck`: timeout 5s per health check

#### Note Tecniche
- **Health check `/health` ResponseWriter**: il callback personalizzato NON viene invocato correttamente (restituisce `text/plain "Degraded"` invece di JSON). DA RISOLVERE.
- OpenTelemetry Console Exporter per sviluppo — vedere trace nel terminale
- Prometheus endpoint su `/metrics` standard — pronto per Grafana/Prometheus scraper
- Correlation ID propaga tra request HTTP, background jobs (via Activity), SignalR
- Serilog config ora rispetta differenze Dev (Debug+Console) vs Prod (Warning+File only)
- Nessun Seq in Fase 1 — Solo configurazione preparatoria in appsettings

---

### [2026-08-22] Feature — Sealed Product Sync (Prezzi Box Automatici)

#### Problema
Il calcolatore box nella pagina Espansioni richiedeva l'inserimento manuale di `BoxPrice` per ogni espansione. Non c'era modo automatico per recuperare i prezzi dei prodotti sigillati (booster box, case, starter deck) dal marketplace Card Trader.

#### Soluzione Implementata

**Backend — nuovi servizi:**
- `SealedCategoryIds` (Domain/Entities): classe statica con HashSet<int> per categoria sigillata per gioco:
  - Magic: The Gathering (GameId=1): {4, 5, 7, 10, 13} — Booster Boxes, Boosters, Starter Decks, Box Sets & Displays, Boxed Set
  - Force of Will (GameId=2): {30, 31, 33, 34}
  - Pokémon (GameId=3): {4576, 4580}
  - Lorcana (GameId=4): {12821, 12825}
  - Metodo `IsSealedCategory(gameId, categoryId)` per check O(1)
- `Blueprint.IsSealedProduct` (Domain/Entities): proprietà calcolata `GameId != 0 && SealedCategoryIds.IsSealedCategory(GameId, CategoryId)`
- `SealedProductPriceService` (Infrastructure/BackgroundJobs): BackgroundService one-shot (pattern identico a `PopulateItalianNamesService`)
  - Abilitato via `SyncSettings:PopulateSealedPricesOnStartup=true` in appsettings.json
  - Recupera tutte le espansioni abilitate (`Game.IsEnabled`)
  - Per ogni espansione: chiama `GetMarketplaceProductsByExpansionAsync(expansion.CardTraderId)`
  - Filtra marketplace products: blueprint con `IsSealedProduct == true` + `Properties.Language == "English"`
  - Per ogni blueprint sealed, prende il prezzo minimo
  - Raccoglie tutti i minimi, ordina ascendente, prende i primi 10
  - Calcola media dei 10 prezzi → `Expansion.BoxPrice` in euro (conversione da centesimi)
  - Log dettagliato per espansione: `Processing`, `Found N sealed products`, `Updated BoxPrice to €X.XX`
  - Delay 500ms tra espansioni per rate limiting
  - Esecuzione one-shot: termina con `Environment.Exit(0)`
- Endpoint manuale: `POST /api/expansions/sync-sealed-prices` in `ExpansionsController` per trigger on-demand da UI

**Frontend:**
- `expansions.service.ts`: aggiunto metodo `syncSealedPrices(): Observable<any>` per chiamare l'endpoint manuale

**Configurazione:**
- `appsettings.json` → `SyncSettings.PopulateSealedPricesOnStartup: false` (default disabilitato)

#### Note Tecniche
- Riutilizza `ICardTraderApiService.GetMarketplaceProductsByExpansionAsync` esistente
- Rate limiting: CardTraderRateLimiter (20 req/min singleton) + delay 500ms tra espansioni
- Se meno di 10 prodotti sigillati trovati, usa quelli disponibili (logga warning)
- Prodotti senza prezzo English vengono ignorati
- Configurazione disabilitata di default; si abilita solo per run one-shot, poi si disabilita
- **Non incluso nella sync notturna automatica**: il processo richiede diversi minuti; si lancia manualmente on-demand (consigliato settimanale) via endpoint `POST /api/expansions/sync-sealed-prices` o abilitando temporaneamente `PopulateSealedPricesOnStartup=true` al riavvio

---

### [2026-08-22] UI — Aumentate dimensioni immagini nel blueprint selector dropdown

**Problema**
Nella maschera "Nuovo Prodotto", il dropdown di ricerca blueprint mostrava immagini troppo piccole (30×42px) rendendo difficile il riconoscimento visivo delle carte.

**Soluzione Implementata**
- `blueprint-selector.component.ts`:
  - Immagine card: 30×42px → 50×70px (+67% larghezza, +67% altezza)
  - Gap tra immagine e dettagli: 12px → 16px
  - Padding verticale option: 4px → 8px
  - min-height option container: 56px → 96px
  - Font nome: default → 1rem, weight 500
  - Font nome italiano: 0.75em → 0.85em
  - Aggiunto box-shadow su immagini per migliore visibilità
  - Panel autocomplete min-width: 400px per accomodare layout più ampio

**Note Tecniche**
- Usa `::ng-deep` per sovrascrivere stili Material Autocomplete
- Modifica non breaking: mantiene stessa struttura dati, solo presentazione

---

## [2026-08-22] Feature — Popolamento nomi italiani blueprint da MTGJSON

### Problema
Scryfall ha nomi italiani solo per ~128 carte (quelle con localizzazione ufficiale pubblicata). Il 99.9% dei blueprint rimaneva senza `ItalianName`, rendendo inutile la ricerca per nome italiano nel selettore "Nuovo Prodotto".

### Soluzione Implementata

**Backend — nuovi servizi:**
- `MtgJsonClient` / `MtgJsonClientFactory`: download e parsing `AllPrintings.json` da MTGJSON
- Match su `identifiers.scryfallId` (ID specifico per stampa) ↔ `Blueprint.ScryfallId` — match esatto 1:1
- Copertura ~95%+ carte con traduzione italiana ufficiale vs ~0.1% Scryfall
- `PopulateItalianNamesService`: background service one-shot (abilitato via `SyncSettings:PopulateItalianNamesOnStartup=true`)
  - Scarica AllPrintings.json (~200MB) una tantum
  - Estrae ~30k+ nomi italiani in memoria (Dictionary lookup O(1))
  - Popola `Blueprint.ItalianName` in batch da 500 con `SaveChangesAsync` per batch
  - Fallback automatico su Scryfall API per carte non coperte da MTGJSON
  - Logging dettagliato: `FromMtgJson`, `FromScryfall`, `NotFound`
  - Esecuzione one-shot: termina con `Environment.Exit(0)` dopo completamento

**Frontend — già pronto:**
- `BlueprintSelectorComponent` usa già `blueprint.italianName` per display e ricerca
- `SearchByNameAsync` in `BlueprintsController` filtra già su `ItalianName`

### Note Tecniche
- `AtomicCards.json` usa `scryfallOracleId` (unico per carta logica) → NON matcha `Blueprint.ScryfallId`
- `AllPrintings.json` usa `identifiers.scryfallId` (unico per stampa) → MATCHA `Blueprint.ScryfallId` direttamente
- Il servizio è disabilitato di default; si abilita solo per run one-shot, poi si disabilita
- Migrazione DB già esistente: `20260820000000_AddItalianNameToBlueprints`

---

## [2026-05-19] Feature — Pannello "Le mie inserzioni" in Nuovo Prodotto + Update CT API

### Problema
Nella maschera "Nuovo Prodotto" non c'era visibilità sulle inserzioni già presenti su Card Trader per la carta selezionata. Era impossibile modificare un'inserzione esistente senza passare dalla griglia coda in fondo alla pagina. Inoltre, modificando un'inserzione già sincronizzata, il sistema creava una nuova inserzione su CT invece di aggiornare quella esistente, causando duplicati e dati sbagliati nei report.

### Soluzione Implementata

**Backend — nuovi comportamenti:**
- `PendingListing`: aggiunto campo `IsUpdate` (bool) — quando `true`, il sync chiama `PUT /products/{id}` su CT invece di `POST /products`
- `CreatePendingListingDto`: aggiunto `CardTraderProductId` e `IsUpdate` per supportare il caso CT-native
- `CardTraderApiClient.UpdateProductOnCardTraderAsync`: implementato (era stub vuoto) — `PUT /products/{id}` con price, quantity, properties
- `PendingListingsController`:
  - Nuovo endpoint `GET /api/pending-listings/by-blueprint/{id}` — restituisce InventoryItems per blueprint con stato PendingListing associato (4 stati: `synced`, `pending-edit`, `ct-native`, `pending-new`)
  - `SyncPendingListings`: distingue CREATE vs UPDATE; dopo UPDATE riuscito aggiorna anche l'`InventoryItem` locale (fix pannello che mostrava valori obsoleti)
  - `UpdatePendingListing`: se la listing era già sincronizzata (`IsSynced=true`), la rimette in coda con `IsUpdate=true` invece di bloccare con 400
  - `CreatePendingListing`: in modalità `IsUpdate=true` salta il check duplicati e salva il `CardTraderProductId`
- Migrazione EF: `20260519073801_AddIsUpdateToPendingListings`

**Frontend — nuova UX:**
- Layout "Nuovo Prodotto" a 3 colonne: immagine | form | pannello "Le mie inserzioni"
- Pannello destro mostra le inserzioni correnti per la carta selezionata (si aggiorna anche con le frecce di navigazione)
- Pulsante "Carica nel form" per ogni inserzione — gestisce 3 casi:
  1. **PendingListing non sincronizzata** → modifica il record esistente
  2. **PendingListing già sincronizzata** → re-accoda con `IsUpdate=true` (il sync aggiornerà CT)
  3. **CT-native** (solo in InventoryItems, mai gestita da noi) → crea nuovo PendingListing con `IsUpdate=true` e `CardTraderProductId` preimpostato
- Banner contestuale nel form che indica la modalità di editing attiva
- Label tasto "Salva" che cambia in base al contesto (Aggiungi / Aggiorna / Aggiungi aggiornamento)

### Note Tecniche
- `InventoryItem` viene aggiornato subito dopo un UPDATE sync riuscito — non bisogna attendere la sync notturna per vedere i valori corretti nel pannello
- Le inserzioni "Solo CT" (`ct-native`) sono quelle presenti in `InventoryItems` ma senza corrispondente `PendingListing` — create direttamente su CT, mai passate dal nostro software
- La distinzione CREATE/UPDATE in `SyncPendingListings` si basa su `IsUpdate && CardTraderProductId != null`

---

## [2026-05-19] Fix Qtà/Valore Rimanente — report Redditività per Tag

### Problema
Le colonne "Qtà Rimanente" e "Valore Rimanente €" nel report Redditività per Tag (entrambi i livelli: per Tag e per Espansione) erano sempre a zero. Analogamente, nella griglia Inventario gli InventoryItem non mostravano il Tag anche se la PendingListing corrispondente ce l'aveva.

### Causa Radice
Le query in `ReportingController` filtravano su `InventoryItem.Tag == X`, ma quel campo è quasi sempre NULL perché:
- L'`InventoryItem` viene creato da `SyncProductsAsync` (sync da Card Trader), che propaga il Tag da `PendingListing` via `CardTraderProductId` **solo** se il product arriva nel batch di sync.
- Prodotti già sincronizzati prima dell'introduzione del sistema di Tag, o che non rientravano nell'ultimo batch di sync, rimanevano con `Tag = null`.
- La source of truth del Tag è `PendingListing.Tag`, non `InventoryItem.Tag`.

### Soluzione Implementata

**`ReportingController`** — due query riscritte:
- `rimanentePerTag`: join `PendingListings → InventoryItems` via `CardTraderProductId` invece di `WHERE InventoryItem.Tag = X`
- `rimanentePerExpansion`: step 1 recupera i `CardTraderProductId` dalla PendingListing con il Tag; step 2 join LINQ `InventoryItems → Blueprints → Expansions` su quell'insieme di ID

**`CardTraderInventoryController`** — aggiunto endpoint `POST /api/cardtrader/inventory/backfill-tags`:
- Copia `PendingListing.Tag` su `InventoryItem.Tag` per tutti gli item dove `CardTraderProductId` corrisponde
- Solo per item con `Tag == null` (non sovrascrive tag impostati manualmente)
- Da chiamare una volta per allineare gli item storici (fix griglia Inventario)

### Note Tecniche
- Il fix del report non richiede backfill: la query legge sempre la source of truth (PendingListings) a runtime
- Il backfill è necessario solo per mostrare il Tag nella griglia Inventario (usa `InventoryItem.Tag` per display)
- `SyncProductsAsync` già fa la propagazione Tag per i nuovi item, il problema era solo per i pre-esistenti

---

## [2026-03-27] Feature 003 (cont.) - Fix Report Redditività per Tag, Backfill Tag, Grid State

### Problema
1. **Bottone sync singolo ordine** non visibile in griglia ordini — le modifiche erano rimaste nel worktree e non erano state copiate nel repo principale.
2. **TotaleAcquistato** a livello Tag (911.54€) non coincideva con la somma delle espansioni di dettaglio (268.94€) — la query pendingPrices usava `OPENJSON` su una lista di `blueprintIds` enorme causando timeout 30s.
3. **TotaleAcquistato** nelle espansioni di dettaglio mostrava il "costo del venduto" invece del totale acquistato reale — doveva usare `SUM(Quantity * PurchasePrice)` da `PendingListings` join `Blueprints` join `Expansions`.
4. **ValoreRimanente** sempre zero — la query usava `InventoryItems.PurchasePrice` (zero) invece di `InventoryItems.ListingPrice`.
5. **Tag mancante su OrderItems storici** — `POST /api/cardtrader/orders/backfill-tags` non matchava perché usava `OrderItem.BlueprintId` (spesso NULL) invece di risolvere via `OrderItem.CardTraderId → Blueprints.CardTraderId → Blueprints.Id`.

### Soluzione Implementata

**Backend:**
- `ReportingController.GetTagProfitability` — sostituita la doppia query (vendutoPerTagBlueprint + pendingPrices con OPENJSON) con un singolo JOIN SQL tra OrderItems e PendingListings (nessuna lista blueprintIds passata come parametro)
- `ReportingController.GetTagExpansionProfitability` — `TotaleAcquistato` ora calcolato con `SUM(pl.Quantity * pl.PurchasePrice)` via JOIN `PendingListings → Blueprints → Expansions`, allineato alla query SQL diretta
- `ReportingController` (entrambi i livelli) — `ValoreRimanente` ora usa `InventoryItems.ListingPrice` (prezzo attuale di mercato) invece di `PurchasePrice`
- `CardTraderOrdersController` — nuovo endpoint `POST /api/cardtrader/orders/backfill-tags` riscritto per risolvere `BlueprintId` tramite `OrderItem.CardTraderId → Blueprints.CardTraderId → Blueprints.Id` + aggiorna anche `OrderItem.BlueprintId` se era NULL

**Frontend:**
- `orders-list.component.ts` e `cardtrader-api.service.ts` — copiati dal worktree al repo principale (la colonna "Azioni" con bottone sync era già implementata ma non pubblicata)
- `TagProfitabilityComponent` — aggiunta gestione completa stato griglia per entrambe le griglie (Tag e Espansioni):
  - Sidebar con pannello "Colonne" per mostrare/nascondere
  - Persistenza in `localStorage` tramite `GridStateService` con ID separati (`tag-profitability-tags-grid`, `tag-profitability-expansions-grid`)
  - Ripristino automatico al caricamento
  - Salvataggio su: spostamento, visibilità, ridimensionamento, ordinamento colonne

### Note Tecniche
- La query `TotaleAcquistato` corretta per espansione: `SELECT e.Name, SUM(pl.Quantity * pl.PurchasePrice) FROM PendingListings pl INNER JOIN Blueprints b ON b.Id = pl.BlueprintId INNER JOIN Expansions e ON e.Id = b.ExpansionId WHERE Tag = @tag GROUP BY e.Name`
- Il backfill tag storici ha limitata copertura perché molti OrderItems storici hanno `CardTraderId` che non trova corrispondenza nei `Blueprints` caricati (blueprint non ancora importati nel sistema locale)

---

## [2026-03-26] Feature 003 - Import Tag Dettaglio Ordini & Report Redditività per Tag

### Problema
1. La colonna `Tag` in `dbo.OrderItems` era vuota o mostrava valori errati — il mapper usava `user_data_field` invece del campo `tag` dell'endpoint dettaglio ordine.
2. `Price` in `OrderItems` era sempre 0 — l'endpoint lista (`GET /orders`) non restituisce `seller_price` per item; serve il dettaglio (`GET /orders/{id}`).
3. La sincronizzazione notturna non includeva gli ordini: `ScheduledProductSyncWorker` impostava `SyncOrders = true` ma `CardTraderSyncOrchestrator` non aveva il relativo blocco.
4. Il report "Redditività per Tag" restituiva 500 dopo 31s di timeout — caricava tutti gli OrderItems in memoria invece di usare GROUP BY SQL.
5. `TotaleAcquistato` sempre 0 — veniva letto da `InventoryItems.PurchasePrice` invece che da `PendingListings.PurchasePrice`.

### Soluzione Implementata

**Domain / Infrastructure:**
- `OrderItem.cs` — aggiunta property `Tag`
- Migration `20260326145049_AddTagToOrderItems` — colonna `Tag` nvarchar nullable con backfill da `UserDataField`
- `CardTraderOrderItemDto` — aggiunti `Tag`, `Price` (nullable), reso nullable `SellerPrice`
- `CardTraderDtoMapper` — mapping corretto: `Tag = dto.Tag`, `Price = (SellerPrice?.Cents ?? Price?.Cents ?? 0) / 100m`
- `ICardTraderApiService` + `CardTraderApiClient` — aggiunto `GetOrderDetailAsync(orderId)`; `GetOrdersAsync` arricchisce ogni ordine con il detail endpoint
- `InventorySyncService` — branch UPDATE ora aggiorna sia `Tag` sia `Price` sugli item esistenti
- `CardTraderSyncOrchestrator` — aggiunto blocco `SyncOrders` + metodo `SyncOrdersAsync` (ultimi 7 giorni); sync notturna ora include gli ordini

**API:**
- `CardTraderOrdersController` — nuovo endpoint `POST /api/cardtrader/orders/{cardTraderOrderId}/sync` per sync singolo ordine
- `ReportingController.GetTagProfitability` — riscritto con GROUP BY SQL; `TotaleAcquistato` da `PendingListings`
- Nuovi DTOs: `TagProfitabilityDto`, `TagExpansionProfitabilityDto`
- Nuovi endpoint: `GET /api/reporting/profitability/by-tag`, `GET /api/reporting/profitability/by-tag/{tag}/expansions`

**Frontend:**
- Nuove interfacce `TagProfitability`, `TagExpansionProfitability` in `reporting.models.ts`
- Nuovi metodi in `reporting.service.ts`
- Nuovo componente `TagProfitabilityComponent` con drill-down per espansione al click di riga
- Route `/profitability/tags` e voce menu "Redditività per Tag"

**Test:**
- `eCommerce.Inventory.Tests.csproj` — corretto target `net8.0`, versioni pacchetti allineate
- `CardTraderSyncOrchestratorTests` — aggiunto mock `IScryfallApiClient` mancante

### Verifica
- Build: 0 errori
- Sync ordini di massa (da inizio anno): Price e Tag popolati correttamente
- Endpoint `/api/reporting/profitability/by-tag` risponde senza timeout

### Note Tecniche
- L'endpoint lista CardTrader (`GET /orders`) non include `seller_price` per item — necessario chiamare il detail per ogni ordine (N+1 API calls, gestito con rate limiter)
- Il `TotaleAcquistato` nel report viene da `PendingListings.PurchasePrice * Quantity`, raggruppato per `Tag`
- Rimane aperta la verifica di coerenza tra valori livello-Tag e livello-Espansione nel report (vedi Features/003)

---

## [2026-02-22] Items to Prepare - Icone Espansione, Date e Miglioramenti UI

### Problema
La griglia "Items to Prepare" mancava di alcune funzionalità chiave per l'efficienza operativa:
1. L'anteprima dell'immagine richiedeva l'hover sul widget immagine, rendendo lenta la navigazione.
2. La checkbox per marcare gli articoli come "preparati" era troppo piccola.
3. Mancava la possibilità di ordinare le carte per data di rilascio dell'espansione.
4. Mancavano riferimenti visivi (icone) per le espansioni.

### Soluzione Implementata

#### Backend & Data Sync
- **Integrazione Scryfall**: Implementato `ScryfallApiClient` per recuperare metadati delle espansioni (data uscita e URL icona SVG).
- **Core Orchestrator**: Modificato `CardTraderSyncOrchestrator` per arricchire i dati Card Trader con i metadati Scryfall durante la sincronizzazione.
- **DTOs & Repositories**: Aggiornati `ExpansionDto`, `UnpreparedItemDto` e `OrderRepository` per gestire e trasportare `ReleaseDate` e `IconSvgUri`.

#### Frontend (Angular & AG-Grid)
- **Hover Row-Level**: L'anteprima dell'immagine ora si attiva al passaggio del mouse su qualsiasi cella della riga.
- **Icone & Date**: Aggiunte colonne "Icon" (SVG Scryfall) e "Rel. Date" alle griglie "Items to Prepare" ed "Expansions".
- **Sorting Multi-Colonna**: Configurato ordinamento predefinito per Data Uscita (ASC) e poi Collector Number (ASC).
- **Nuovo Pulsante Prepare**: Sostituita la checkbox con un pulsante verde ad alta visibilità.

### Verifica
- Build API ed Infrastructure completate con successo.
- Testata l'integrazione API con Scryfall (endpoint `/sets`).
- Verifica visiva delle icone e dell'ordinamento nelle griglie AG-Grid.

---

## [2026-02-06] Create Listing - Prezzi Suggeriti Filtrati

### Problema
I prezzi suggeriti (Min, Medio, Max) nella maschera di creazione prodotto erano generici per il Blueprint, senza considerare condition, lingua o flag foil/signed, portando a suggerimenti poco accurati. Inoltre, inserzioni placeholder con prezzi esorbitanti falsavano le statistiche.

### Soluzione Implementata

**`CardTraderInventoryController.cs`**
- Endpoint `GetMarketplaceStats` ora supporta filtri opzionali: `condition`, `language`, `isFoil`, `isSigned`.
- Implementato tetto massimo di **1000€** per ignorare inserzioni "preliminari" o fuori mercato.
- **Fix**: Gestione robusta dei `JsonElement` per l'estrazione delle proprietà `condition` e `language`, risolvendo il problema dei prezzi suggeriti a zero.

**`cardtrader-api.service.ts`**
- Passaggio dei filtri come parametri HTTP.

**`create-listing.component.ts`**
- Refresh automatico delle statistiche di mercato al variare dei campi rilevanti del form (usando RxJS per debouncing).

### Verifica
- Suggerimenti di prezzo ora riflettono la configurazione selezionata della carta.
- Eliminazione del rumore statistico causato da prezzi sopra i 1000€.

---

## [2026-02-06] Blueprint Sync - Fix Aggiornamento Dati Esistenti

### Problema
I Blueprints vengono inseriti correttamente durante la sincronizzazione da Card Trader, ma i record esistenti non vengono aggiornati con i nuovi dati. Questo causa URL immagini obsolete (es. `preview_winnowing-lorwyn-eclipsed(2).jpg` invece della versione finale).

### Causa Radice
La funzione `UpsertBlueprintsAsync` in `CardTraderSyncOrchestrator.cs` aggiornava solo 4 campi (`Name`, `Rarity`, `Version`, `ExpansionId`) ma ignorava altri 10 campi importanti.

### Soluzione Implementata

**`CardTraderSyncOrchestrator.cs`**
- Aggiunto aggiornamento per tutti i campi rimanenti:
  - `ImageUrl`, `BackImageUrl`
  - `CategoryId`, `GameId`
  - `FixedProperties`, `EditableProperties`
  - `CardMarketIds`, `TcgPlayerId`, `ScryfallId`
  - `UpdatedAt` (timestamp aggiornamento)

**`CardTraderSyncOrchestratorTests.cs`**
- Aggiunto mock per `IExpansionAnalyticsService`
- Nuovo test: `SyncAsync_SyncBlueprints_ShouldUpdateExistingBlueprints`

### Verifica
- Il progetto Infrastructure compila con successo (0 errori)
- Il fix aggiorna tutti i campi durante la sincronizzazione notturna e manuale

### Note Tecniche
- La sincronizzazione notturna (`ScheduledProductSyncWorker`) era già attiva e funzionante
- Il fix si applica sia alla sync manuale che a quella schedulata

---

## [2025-12-23] Expansion Analytics - Fix Calcolo Valori

### Problema
L'analisi del valore delle espansioni mostrava valori identici per "Valore Medio" e "Valore Totale", o falliva con errore 400 Bad Request.

### Causa Radice
1. **Errore 400**: L'API Card Trader non supporta il parametro `blueprint_id[]` per batch requests sull'endpoint `marketplace/products`.
2. **Valori Identici**: Senza le parentesi, l'API elaborava solo il primo ID, risultando in `cardCount = 1`.
3. **Valori Gonfiati**: I prodotti non-carta (box, fat pack, ecc.) venivano inclusi nel calcolo.

### Soluzione Implementata

#### Approccio: Fetch per Espansione
Invece di richiedere i prezzi carta per carta in batch, ora si usa il filtro `expansion_id` che restituisce tutti i prodotti dell'espansione in una singola chiamata.

#### File Modificati

**`ICardTraderApiService.cs`**
- Aggiunto metodo `GetMarketplaceProductsByExpansionAsync(int expansionId)`.

**`CardTraderApiClient.cs`**
- Implementato `GetMarketplaceProductsByExpansionAsync` usando `marketplace/products?expansion_id={id}`.
- Refactoring di `GetMarketplaceProductsBatchAsync` per usare chiamate parallele singole come fallback.

**`CardTraderMarketplaceProductDto.cs`**
- Aggiunto campo `PropertiesHash` per catturare le proprietà extra dal JSON.

**`ExpansionAnalyticsService.cs`**
- Sostituito il loop di batching con una singola chiamata per espansione.
- Aggiunto filtro `tournament_legal` per escludere prodotti non-carta.
- Aggiunto log di debug con generazione CSV per audit dei calcoli.

### Verifica
- L'analisi per "Journey into Nyx" ora mostra valori corretti e distinti.
- I log mostrano il conteggio dei prodotti filtrati.
- Il file `debug_expansion_{id}.csv` viene generato per audit.

### Note Tecniche
- Il parametro `tournament_legal` in `properties_hash` è presente solo per le carte da gioco.
- La risposta dell'API è un dizionario con chiavi = blueprint_id.

---

## Template per Future Voci

```markdown
## [YYYY-MM-DD] Titolo Feature/Fix

### Problema
Descrizione del bug o della feature richiesta.

### Causa Radice
Analisi tecnica del problema.

### Soluzione Implementata
- File modificati
- Approccio tecnico
- Eventuali workaround

### Verifica
Come è stato testato e validato.

### Note Tecniche
Dettagli utili per future manutenzioni.
```
