# eCommerce.Inventory - Contesto di Sessione

> Punto di partenza per ogni nuova sessione con Claude.
> Aggiornare dopo ogni sessione significativa.
> **Non è un documento di architettura** (quello è ARCHITECTURE.md) né uno storico (quello è CHANGELOG.md): è lo stato di oggi, per riprendere velocemente senza rileggere tutto. Quando una voce è chiusa, va tolta da qui.

---

## Stato Attuale

**Branch principale:** `master`
**Ultimo aggiornamento:** 2026-10-15
**Fase:** In produzione (uso quotidiano attivo)

### Cosa funziona adesso

**Piattaforma**
- Backend API come servizio Windows su `http://localhost:5152`, frontend Angular su IIS (`http://inventory.local`), SQL Server Express. Backup giornaliero automatico di database e applicazione
- Utente unico `admin` con JWT. **Ogni endpoint richiede il ruolo `Admin`** per criterio globale (`FallbackPolicy` in `Program.cs`). Le eccezioni sono dichiarate una per una: login, webhook Card Trader (firma HMAC obbligatoria), `/health`, `/health-ui`, `/metrics` e l'hub SignalR. La registrazione non esiste: gli account si creano a mano sul database. La password si cambia da `POST /api/auth/change-password`
- Le migration si applicano da sole all'avvio, in ogni ambiente
- Osservabilità: `/health` (database, Card Trader, Redis), `/metrics` Prometheus, tracing OpenTelemetry su console, Correlation ID nei log Serilog
- Rate limiter verso Card Trader (20 richieste al minuto, condiviso da tutta l'applicazione)

**Card Trader: sincronizzazione, inserzioni, ordini**
- Sincronizzazione di giochi, espansioni, blueprint, inserzioni e ordini, notturna (`ScheduledProductSyncWorker`) e manuale. Un fallimento di sezione marca l'intera sincronizzazione come fallita
- "Nuovo Prodotto": ricerca per nome, nome italiano e numero di raccolta; descrizione Card Trader; pannello "Le mie inserzioni" per modificare quelle esistenti, con grafico dello storico prezzi e riferimento di mercato; link a Card Trader. Il prezzo d'acquisto si riempie dal Tag dell'acquisto
- Storico prezzi (`PriceHistoryEntries`) alimentato dalla sincronizzazione notturna, solo quando prezzo o quantità cambiano
- Ordini con griglia "Articoli da preparare". Card Trader Zero: le carte si preparano giorno per giorno dagli ordini `hub_pending`, e l'ordine "Ct connect" raccolto eredita la preparazione riga per riga
- Il webhook Card Trader non è raggiungibile dall'esterno (scelta, vedi sotto): l'inventario resta allineato con il giro a polling

**Autopricer** (`/layout/pricing`), in modalità reale. Dettaglio in ARCHITECTURE.md
- Regole per fascia di prezzo con collocazione fra i venditori, scarto delle offerte anomale (MAD e rapporto sulla mediana), conversione fra prezzo di vetrina e incasso (sovrapprezzo Card Trader ricavato come differenza), guardrail asimmetrico con soglia in euro sotto cui non scatta
- Regole a ripiego: più regole sulla stessa fascia, provate in ordine di priorità quando il mercato non basta, con minimo di offerte e filtro venditori propri
- Carte "nuove", cioè mai prezzate: fascia ricalcolata sul prezzo proposto, niente guardrail
- Notturna alle 01:00 (`AutoPricing:RunTime`, si cambia solo nel file e serve un riavvio): carte di valore ogni notte, bulk a rotazione (2.000 blueprint a notte, ciclo di circa 9 giorni). Riprezzo subito dopo la pubblicazione dalla maschera. Esecuzione manuale in background con indicatore in alto
- Schede Regole, Anteprima (filtrabile, "Applica le selezionate"), Esecuzioni (filtro per Origine), Storico (griglia dei calcoli carta per carta, "Applica comunque" sulle bloccate dal guardrail)
- **Profilo in produzione, scelto dall'utente:** rialzo massimo 10.000%, ribasso massimo 80%, variazione libera 0,15 €, minimo 4 offerte comparabili, solo venditori Card Trader Zero. "N-esima più bassa" in tutte le fasce (2ª offerta, 3ª sopra 100 €), con ripieghi configurati

**Pagina "Acquisti": analisi dei prodotti sigillati.** Dettaglio in `Documentation/Features/004-AnalisiAcquistoProdotti.md`
- Dati: listino pubblico Cardmarket ogni mattina alle 07:00 (`CardmarketImportWorker`); catalogo sigillati e composizione delle buste da MTGJSON
- Analisi uscita: convenienza fra formati, valore atteso dell'apertura (prezzi CM e CT affiancati, fattore "prezzo realizzato", quota venduta per fascia, costo per carta, costi di vendita 15%), decisione Apri / Tieni sigillato / Dati incompleti / Prezzo CM dubbio / Non giocabili. Trend CM fino a 0,05 € = prezzo di riempimento, quindi "Prezzo CM dubbio"
- Opportunità: classifica giornaliera di tutte le uscite, filtro "Resa massima %", piano d'acquisto su Card Trader per venditore
- Aperture (bilancio reale per Tag) e Registro acquisti (ricerca su tutto il catalogo per nome Cardmarket, costo per carta, previsione salvata all'apertura)
- Avvisi: regole per prodotto o per gruppo, campanella ed email di riepilogo (SMTP configurato). Gli avvisi di apertura ignorano le rese oltre il 300%
- Secret Lair: retrospettiva dei drop comprati; negozio Wizards letto alle 8, 14 e 20 con avviso per ogni superdrop nuovo; valutazione del drop prima dell'acquisto (Compra / Al limite / Lascia)

**Report e altre pagine**
- Dashboard (Overview e Redditività per Tag), Report Vendite, Report Inventario (slow-movers), Redditività per Tag con dettaglio per espansione. Il costo d'acquisto viene da un servizio unico (`PurchaseCostService`): delle modifiche fatte dalla maschera contano solo le copie aggiunte
- Espansioni: calcolatore box con colonna ROI Box%, "Sync Box Prices" da Card Trader, icone e date da Scryfall
- Copia del valore di una cella con doppio clic in tutte le griglie. Colonne salvate in automatico, tranne Inventario, Ordini e Articoli da preparare (salvataggio da menu)

### Cosa è in sospeso / da verificare

- **Feature "Magazzino da tenere" (progettata il 2026-10-15):** documento in `Documentation/Features/005-MagazzinoDaTenere.md`, fasi in ROADMAP. La **Fase 0** (decisione "Nessuna offerta" e resa sul prezzo a cui si compra davvero) è pubblicata e verificata il 2026-10-15. La **Fase 1** (punteggio e vista "Da tenere" nelle Opportunità, migration `AddHoldScore`) è pubblicata e verificata il 2026-10-15: 47 candidati, Secret Lair esclusi per scelta dell'utente. La **Fase 2** (regola d'avviso "Da tenere", soglia 70) è pubblicata e verificata il 2026-10-15. Pubblicata lo stesso giorno la correzione del testo degli avvisi, che si tagliava a metà riga a 2.000 caratteri anche nell'email: da controllare sul prossimo avviso con molti prodotti che finisca con "… e altri N prodotti". La **Fase 3** (casella "Da tenere" nel registro e scheda "Magazzino" in fondo alla pagina Acquisti, migration `AddHoldForResale`) è pubblicata il 2026-10-15, con il primo acquisto segnato "Da tenere" dall'utente. Pubblicati e verificati lo stesso giorno: "Apri (se lo hai già)" per le rese negative, e prezzi Card Trader del magazzino ogni mattina e dal pulsante "Aggiorna"

- **Secret Lair, stima congelata:** dopo l'uscita dei Masters of the Universe (03/11) confrontare la stima con i prezzi reali, colonna "Scarto stima"
- **Box Star Trek (uscita 13/11):** MTGJSON non ha ancora la composizione delle buste TRK/TRC, quindi niente previsione. All'apertura l'utente segna la data sull'acquisto e usa il Tag `#TRK_PB_AAAAMMGG` / `#TRK_CB_AAAAMMGG`, anche sulle inserzioni
- **Revisione settimanale degli avvisi:** il lunedì alle 08:00 un'attività di Claude scrive il rapporto in `Revisioni-avvisi/` (locale, fuori da git). Le regole nuove si creano solo dopo l'ok dell'utente. Da seguire:
  - la regola 4 è a 30 giorni in via temporanea: riportarla a 90 a gennaio 2027
  - l'abbinamento Cardmarket dei prodotti di Mystery Booster Commander (MBC, 09/11)
- **Riserva dell'utente sul grafico in "Le mie inserzioni":** sulle carte con molte inserzioni diverse la colonna è affollata. Nessuna modifica programmata
- **Webhook Card Trader in stand-by:** esporlo richiederebbe di aprire una porta sul router di casa. Non vale il rischio finché il polling basta. Come riprenderlo è in ROADMAP
- **Redis non installato** (`Redis:Enabled = false`): il codice di caching è intatto, si riattiva installando Redis e rimettendo il flag
- **OpenTelemetry solo su console:** nessun backend di raccolta dei trace
- **Backfill dei Tag sugli ordini storici:** copertura parziale, molti `CardTraderId` non hanno un blueprint locale

---

## Decisioni Recenti

> Solo le ultime settimane. Le decisioni tecniche durature sono in ARCHITECTURE.md ("Decisioni Tecniche Rilevanti"), il percorso in CHANGELOG.md.

| Data | Decisione | Perché |
|------|-----------|--------|
| 2026-10-15 | Resa dell'apertura sul trend, o sul low se è più alto; "Nessuna offerta" se su Cardmarket nessuno vende il prodotto | Al trend non si può comprare se l'offerta più bassa è sopra, e senza offerte non si può comprare affatto: le rese del 100–500% di Duel Deck e mazzi Salvat venivano da qui |
| 2026-10-15 | Magazzino da tenere con orizzonte di 1–3 anni, punteggio dal "pavimento" (valore delle carte / prezzo), dall'età dell'uscita e dalla posizione nello storico | Scelte dell'utente. Le medie a 30 giorni di Cardmarket non esistono per i sigillati e il nostro storico parte dal 07/10: il pavimento è il segnale affidabile già oggi |
| 2026-10-12 | Trend Cardmarket fino a 0,05 € trattato come prezzo di riempimento ("Prezzo CM dubbio") e avvisi di apertura con tetto al 300% di resa | Cardmarket mette 0,02 € sui prodotti che non si scambiano da anni: finivano in "Apri" con rese di milioni di % e negli avvisi |
| 2026-10-12 | "Apri" anche con resa negativa | Non è un difetto: la decisione confronta il netto dell'apertura con il netto della rivendita del sigillato (per chi il prodotto ce l'ha già), la resa confronta l'apertura con il prezzo d'acquisto. Gli avvisi chiedono una resa positiva |
| 2026-10-09 | Ordini `hub_pending` di "Ct connect" esclusi da venduto e quote vendute | Sono doppioni degli ordini "Ct connect" pagati: raddoppiavano il venduto delle uscite recenti |
| 2026-10-08 | Secret Lair: drop comprato intero = tutte le carte caricate (almeno 3), o registrato; prezzo standard per tipo (34,99 / 44,99 / 149 / 179 €) | L'utente compra i drop interi da Wizards; le carte dei drop incompleti arrivano da lotti o scambi. Il prezzo pagato non è nei dati, e un prezzo da inserire a mano per 86 drop non sarebbe mai stato compilato |
| 2026-10-08 | Negozio Secret Lair: catalogo da StoreSearch di Scalefast, carte dall'HTML della pagina prodotto; un avviso per superdrop nuovo, nessuno alla prima lettura | Il catalogo non è nell'HTML. Fonte non documentata, scelta consapevole dell'utente. Le carte sono nella pagina prima dell'uscita: è l'unico modo di valutare un drop prima di comprarlo |
| 2026-10-08 | Nessuna penalità specifica per i mazzi Commander | Le vendite reali non mostrano che rendano meno; quello che mancava era il lavoro per carta, ora coperto dal costo per carta |
| 2026-10-08 | Prodotti `memorabilia` di MTGJSON con decisione "Non giocabili" | Carte dal bordo dorato o non ammesse a torneo: nessuna vendita conferma il trend CM |
| 2026-10-08 | Carta "nuova" per l'autopricer = mai prezzata secondo lo storico; fascia ricalcolata una volta sul prezzo proposto; le modifiche dalla maschera restano col guardrail | Scelte dell'utente: se il riprezzo dopo la pubblicazione salta, la notturna deve trattarla ancora da nuova; una carta già in vendita va protetta dai ribassi bruschi |
| 2026-10-08 | Regole a ripiego come catena sulla stessa fascia; il ripiego scatta solo per mercato insufficiente, mai per guardrail | Il minimo di 4 offerte Card Trader Zero lasciava ferme 483 carte. Aggirare il guardrail con un'altra regola lo svuoterebbe di senso |
| 2026-10-07 | Analisi acquisti: listino pubblico Cardmarket (niente API né scraping), contenuto da MTGJSON, classifica salvata ogni giorno, avvisi con stato e un'unica email per giro, piano d'acquisto su Card Trader solo a richiesta | Le motivazioni complete sono nel documento 004 |
| 2026-10-07 | Tag delle inserzioni per apertura, formato `CODICE_TIPO_AAAAMMGG` | Il tag fisso per espansione (es. `#FRA_OLD`) non dice quanti box sono stati aperti né cosa ne è uscito, e mescola Play e Collector |
| 2026-10-07 | Acquisti dell'utente scritti direttamente a database, mai in una migration | Il repository è pubblico |
| 2026-10-07 | Nuove regole di avviso proposte dalla revisione settimanale, create solo dopo l'ok dell'utente | Scrivono in produzione e decidono quali email arrivano |

---

## Punti di Attenzione

- **Il database di sviluppo è quello di produzione** (`eCommerceInventory` anche in `appsettings.Development.json`). Per provare codice con migration, o un'operazione rischiosa, si lavora su una copia ripristinata dal backup (procedura in fondo)
- **Un fix fatto in un worktree va pubblicato dopo averlo portato sul `master` di `C:\Lavoro\eCommerce.Inventory`.** `publish.ps1` compila la cartella da cui parte, e il servizio gira da `Publish\api` di quella cartella. Il 12/10 una pubblicazione dal checkout principale, rimasto indietro, ha lasciato fuori dalla produzione fino al 15/10 un fix già "verificato". Prima di lavorare: `git log master..origin/master`
- **Pubblicare dopo le 09:30:** il riavvio del servizio interrompe la sincronizzazione notturna se è ancora in corso
- **Le migration si applicano da sole all'avvio.** Il log elenca quelle da applicare prima di farlo. Generarle con `dotnet ef migrations add ... --context ApplicationDbContext` (il progetto ha due contesti per via di Health Checks UI)
- **⚠️ Migration con colonne bool o con default:** EF le aggiunge con default `false`/0, quindi le righe già a database non ereditano il default definito in C#. Serve un `UPDATE` esplicito o il default scritto a mano nella migration
- **⚠️ `POST /api/cardtrader/sync/products` e `/orders` non scrivono a database:** restituiscono solo un conteggio. Per una sincronizzazione reale serve `POST /api/cardtrader/sync` con i flag della sezione (`{"syncInventory": true, ...}`)
- **Ogni endpoint nuovo nasce protetto.** Uno che deve stare aperto (probe, webhook, callback) va marcato `[AllowAnonymous]`, ed è lì che va deciso cosa lo autentica al posto del token
- **⚠️ Il dry-run si decide in due punti:** `AutoPricingService.RunAsync` calcola la modalità, ma `PricingEngine` guarda `profile.DryRun` per conto suo. Chi tocca la modalità di scrittura deve considerarli entrambi. `forceApply` non prevale mai su `forceDryRun`, così l'anteprima non scrive mai; «Applica» invece scrive anche col profilo in dry-run, ed è voluto
- **Una sola esecuzione dell'autopricer alla volta** (`PricingRunCoordinator` singleton): il limite di 20 richieste al minuto è condiviso, due esecuzioni si dimezzerebbero a vicenda. Una richiesta rifiutata riceve `409`
- **⚠️ I servizi one-shot chiudono il processo:** `PopulateItalianNamesService` e `SealedProductPriceService` terminano con `Environment.Exit(0)`. **Mai abilitarli in `appsettings.Production.json`**, pena lo spegnimento del servizio a ogni avvio: si lanciano dagli endpoint dedicati (es. `POST /api/expansions/sync-sealed-prices`)
- **⚠️ Non commentare il wiring di produzione in `Program.cs` per i test locali** (`UseWindowsService`, `UseUrls`, `BackupService`, `ScheduledProductSyncWorker`): si usa `ASPNETCORE_ENVIRONMENT=Development` (porta 5155). Commentarlo una volta ha lasciato la produzione senza servizio e senza backup per due giorni
- **Il seed crea `admin` solo se è configurata `Seed:AdminPassword`**, e solo in Development. Il profilo di pricing predefinito invece si crea in ogni ambiente, in dry-run
- **Il JWT dura 7 giorni e non è revocabile.** La chiave di firma non è nei file versionati, e una validazione all'avvio rifiuta il segnaposto o una chiave troppo corta
- **SQL a mano:** `DATEADD` su una stringa `'AAAA-MM-GG'` dipende dalla lingua del server (12/10 letto come 10/12). Usare `CAST('AAAAMMGG' AS date)`
- **Repository pubblico:** mai acquisti, prezzi pagati o venditori dell'utente in documentazione, test o migration
- Il nome italiano dei blueprint si popola lazy da Scryfall durante la sincronizzazione, solo per le carte stampate in italiano
- Il file `debug_expansion_{id}.csv` generato da `ExpansionAnalyticsService` non va committato
- L'AI Grading è un mock: Ximilar richiede un abbonamento a pagamento

---

## Come Riprendere il Lavoro

Prompt consigliato per iniziare una nuova sessione:

```
Leggi CONTEXT.md, ARCHITECTURE.md e ROADMAP.md per capire dove siamo.
Poi [descrivi il task da fare].
```

---

## File Chiave da Conoscere

| File | Perché è importante |
|------|---------------------|
| `eCommerce.Inventory.Api/Program.cs` | Bootstrap DI, middleware, configurazione |
| `eCommerce.Inventory.Infrastructure/Persistence/ApplicationDbContext.cs` | DbContext + DbSet |
| `eCommerce.Inventory.Infrastructure/DependencyInjection.cs` | Registrazione servizi Infrastructure |
| `eCommerce.Inventory.Infrastructure/CardTrader/CardTraderSyncOrchestrator.cs` | Orchestrazione sync completa Card Trader |
| `eCommerce.Inventory.Infrastructure/Persistence/Repositories/BlueprintRepository.cs` | `SearchByNameAsync` — ricerca blueprint (nome, espansione, collector_number, nome IT) |
| `eCommerce.Inventory.Infrastructure/ExternalServices/Scryfall/ScryfallApiClient.cs` | Client Scryfall (set + card per `ItalianName`) |
| `eCommerce.Inventory.Api/Controllers/CardTrader/CardTraderBlueprintsController.cs` | Endpoint blueprint (search) |
| `eCommerce.Inventory.Api/Controllers/ReportingController.cs` | Endpoint reporting (query SQL pesanti) |
| `eCommerce.Inventory.Api/Controllers/ExpansionsController.cs` | Gestione espansioni + calcolatore box (BoxConfigDto, BoxRoiPercentage); costo da `PurchaseCostService` |
| `eCommerce.Inventory.Api/Controllers/PurchasingController.cs` | Analisi acquisti: uscite, analisi, opportunità, piano d'acquisto, aperture, registro, avvisi |
| `eCommerce.Inventory.Infrastructure/Services/SealedProductAnalysisService.cs` | Scomposizione dei sigillati in buste, convenienza fra formati, valore atteso e decisione |
| `eCommerce.Inventory.Infrastructure/Services/OpeningValueCalculator.cs` | Valore atteso di una busta e di un prodotto (probabilità × prezzo, bulk, fattore prezzo realizzato) |
| `eCommerce.Inventory.Infrastructure/Services/PurchaseCostService.cs` | Unica fonte del costo d'acquisto: delle modifiche dalla maschera contano solo le copie aggiunte |
| `eCommerce.Inventory.Infrastructure/Services/AlertService.cs` | Valutazione delle regole di avviso e invio delle notifiche |
| `eCommerce.Inventory.Infrastructure/BackgroundJobs/CardmarketImportWorker.cs` | Giro giornaliero: listino CM, catalogo e dati buste MTGJSON, classifica opportunità, avvisi |
| `eCommerce.Inventory.Api/HealthChecks/` | Health check endpoint `/health` (DatabaseHealthCheck, CardTraderApiHealthCheck, RedisHealthCheck) |
| `eCommerce.Inventory.Api/appsettings.json` | Configurazione (senza segreti) |
| `publish.ps1` | Script deploy automatizzato (richiede permessi Admin) |
| `ecommerce-inventory-ui/src/app/shared/components/blueprint-selector/blueprint-selector.component.ts` | Selector carta in "Nuovo Prodotto" (autocomplete con nome IT) |
| `ecommerce-inventory-ui/src/app/features/expansions/pages/expansions-page.component.ts` | Pagina espansioni con calcolatore box e stato dell'import del listino Cardmarket |
| `ecommerce-inventory-ui/src/app/features/purchasing/pages/purchasing-page.component.ts` | Pagina "Acquisti" a schede (componenti delle schede in `features/purchasing/components/`) |
| `ecommerce-inventory-ui/src/app/features/inventory/pages/dashboard/` | Dashboard principale (2 tab) |
| `ecommerce-inventory-ui/src/app/features/reporting/pages/` | Report Vendite, Inventario, Tag |

---

## Ambiente di Sviluppo

| Voce | Valore |
|------|--------|
| Database | `eCommerceInventory` su `.\SQLEXPRESS` (lo stesso della produzione) |
| Backend (prod) | `http://localhost:5152`, servizio da `Publish\api` |
| Backend (dev) | `http://localhost:5155` |
| Frontend (prod) | `http://inventory.local` (IIS) |
| Frontend (dev) | `http://localhost:4200` |
| Log backend | `Publish/api/logs/` (livello `Information` in produzione, `Debug` in dev) |
| Diagnostica Serilog | `Publish/api/serilog-selflog.txt` — riporta il motivo per cui un sink non parte |
| Servizio Windows | `eCommerce.Inventory`, avvio automatico, account `NT AUTHORITY\NetworkService` |

> **Diagnosticare su una copia, senza toccare la produzione:**
>
> 1. Ripristinare l'ultimo `.bak` da `E:\Applicazioni\eCommerce.Inventory\Backups` come database
>    separato, con `MOVE` dei file su nomi nuovi.
> 2. Puntarci `appsettings.Development.json` e disattivare `Backup:Enabled`, per non sporcare i
>    backup veri.
> 3. Lanciare l'operazione sospetta sull'istanza di sviluppo (porta 5155). Il servizio Windows di
>    produzione resta intatto.
> 4. **Rimettere a posto**: ripristinare la connection string di sviluppo ed eliminare la copia.
>    Sono circa 650 MB, e senza questo passo restano lì a tempo indeterminato.
>
> Attenzione: le migration si applicano da sole, quindi avviare l'istanza di sviluppo contro la
> copia la aggiorna allo schema corrente. Va bene per riprodurre un problema sul codice nuovo,
> non per esaminare lo stato in cui era il database.
