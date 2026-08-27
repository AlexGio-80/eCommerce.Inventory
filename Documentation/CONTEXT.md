# eCommerce.Inventory - Contesto di Sessione

> Punto di partenza per ogni nuova sessione con Claude.
> Aggiornare dopo ogni sessione significativa.
> **Non è un documento di architettura** (quello è ARCHITECTURE.md) — è uno snapshot dello stato corrente per riprendere velocemente senza rileggere tutto.

---

## Stato Attuale

**Branch principale:** `master`
**Ultimo aggiornamento:** 2026-08-27 (sessione 9 — Monitoring Fase 1, ripristino produzione, Autopricer custom)
**Fase:** In produzione (uso quotidiano attivo)

### Cosa funziona adesso
- Autenticazione JWT completa (login `admin` / `admin123` di default)
- Sincronizzazione Card Trader: Games, Expansions, Blueprints, Products, Orders
- Sincronizzazione notturna schedulata (`ScheduledProductSyncWorker`)
- Frontend Angular completo su `http://inventory.local` (IIS)
- Backend API come Windows Service su `http://localhost:5152`
- Creazione inserzioni su Card Trader (Create Listing con price suggestions filtrate)
- **Pannello "Le mie inserzioni" in Nuovo Prodotto** — mostra inserzioni esistenti per carta selezionata, permette di caricarle nel form e modificarle (update su CT, non nuova inserzione)
- Gestione ordini con griglia "Items to Prepare" e sync manuale/auto
- **Dashboard con 2 tab**: Overview (KPI + Vendite per Espansione + ROI) + Redditività per Tag
- Widget "Ultimo Sync" legge da `localStorage` (valorizzato ad ogni sync completata)
- **Report Vendite** con filtro date, griglia top prodotti AG Grid con stato persistente
- **Report Inventario** con soglia giorni configurabile per slow-movers, AG Grid, 4 KPI (valore totale, articoli totali, prodotti unici, valore medio)
- Report Redditività per Tag con drill-down per Espansione
- **Calcolatore Box su pagina Espansioni**: PacksPerBox, CardsPerPack, BoxPrice salvati a DB; ROI% e breakeven calcolati on-the-fly; colonna **ROI Box%** in griglia con colore verde/arancio/rosso, filtrabile e ordinabile
- **Sealed Product Sync**: auto-popola `BoxPrice` da Card Trader (primi 10 prezzi più bassi in inglese tra Blueprint categoria "sealed") — via button "Sync Box Prices" in pagina Espansioni o flag `PopulateSealedPricesOnStartup` all'avvio
- Expansion Analytics (valore medio carte per espansione via Card Trader API)
- AI Grading mock (Ximilar API non attivata)
- **Redis caching per dati statici Card Trader**: Games (TTL 24h), Expansions (TTL 12h), Blueprints (TTL 6h), Categories (TTL 24h) — riduce chiamate API durante sync e velocizza frontend
- **Health check endpoint `/health`**: controlla DB (SQL Server), Card Trader API (via cached Games), Redis (se abilitato) — per liveness/readiness probes in produzione. Risponde in ~0,2s con HTTP 200. Il fallimento di Card Trader produce `Degraded`, non `Unhealthy`: un servizio esterno giù non deve far risultare down la nostra applicazione
- **Autopricer custom** (`/layout/pricing`): motore di pricing a regole alternativo a quello nativo di Card Trader. Regole per fascia di prezzo con posizionamento fra i venditori, scarto statistico degli outlier (MAD) al posto del filtro sulle recensioni che l'API non espone, guardrail su prezzo minimo e variazione massima, dry-run come modalità del profilo. Esecuzione notturna con copertura a rotazione (carte di valore ogni notte + fetta di bulk) e reprice immediato dopo una vendita via webhook. Ogni valutazione è registrata con il motivo, applicata o meno
- **Monitoring/Observability Fase 1**: endpoint `/metrics` Prometheus (runtime + HTTP + 20 metriche business in `Application/Metrics/BusinessMetrics.cs`), distributed tracing OpenTelemetry (ASP.NET Core, HttpClient, EF Core) con Console exporter, middleware Correlation ID (`X-Correlation-ID` propagato + enrichment Serilog con `CorrelationId`/`TraceId`/`SpanId`), Serilog configurato da appsettings per environment, Health Checks UI su `/health-ui`
- Rate limiter outbound Card Trader (20 req/min)
- Backup giornaliero automatico (DB + applicazione)
- Icone espansioni e date rilascio da Scryfall
- **Ricerca blueprint per Collector Number e Nome Italiano** nel selector "Nuovo Prodotto": il campo di ricerca ora matcha anche `collector_number` (da FixedProperties JSON) e il nome italiano (popolato lazy via Scryfall `localized.it` durante la sync blueprint); l'autocomplete mostra il nome italiano sotto quello inglese quando disponibile
- **PurchasePrice preservato dopo sync notturna**: il mapper `MapProductToInventoryItem` ora accetta un `purchasePrice` opzionale da `PendingListing` e lo propaga sull'`InventoryItem` creato dalla sync; sia `InventorySyncService.SyncProductsAsync` che `CardTraderSyncOrchestrator.UpsertInventoryAsync` fanno lookup del `PurchasePrice` (e `Tag`) da `PendingListing` per il `CardTraderProductId` corrispondente. Risolve il problema per cui il giorno dopo la sync il campo `PurchasePrice` risultava vuoto nel pannello "Le mie inserzioni".
- **Campo Descrizione Card Trader in "Nuovo Prodotto"**: scrittura e lettura del campo `description` di Card Trader (es. "Timbro dei nazionali Italiani"), sostituisce il campo "Posizione" nel form. `GET /api/v2/products/{id}` per leggere la descrizione esistente; payload CREATE/UPDATE su CT ora include `description`; `Description` su `PendingListing` e `InventoryItem` (migration `20260820202633_AddDescriptionToEntities`, applicata). Form UI: "Posizione" → "Descrizione" con hint "Descrizione visibile su Card Trader".

### Cosa è in sospeso / da verificare

- **⚠️ Punti di sicurezza aperti (2026-08-27)**, tutti preesistenti — dettaglio e decisioni da prendere in ROADMAP.md, sezione "Da Fare":
  - `POST /api/auth/register` è raggiungibile **senza autenticazione** (`AuthController` non ha `[Authorize]`)
  - Nessun `[Authorize(Roles = "Admin")]` nel progetto: qualunque utente autenticato accede a tutto
  - La password di `admin` è `admin123`, valore scritto nella documentazione di un repository pubblico
  - Utente `testuser` residuo dai test iniziali
  - **Risolto il 2026-08-27**: la chiave di firma JWT non è più nei file versionati, ed esiste una validazione all'avvio che impedisce di partire con il segnaposto o con una chiave troppo corta
- **FIXED (2026-08-24)**: Disallineamento `TotaleAcquistato` tra Tag e Espansione nel report Redditività — causato da query Tag che usava raggruppamento diretto su `PendingListings.Tag` (includeva record con Blueprint/Expansion NULL) vs query Espansione che usava navigation properties con INNER JOIN implicito (escludeva quei record). Risolto uniformando `GetTagExpansionProfitability` a usare join espliciti (`from pl join bp join ex`) come già fatto per `rimanentePerExpansion`.
- **Redis non è installato sulla macchina**: `Redis:Enabled` è ora `false`. Tutto il codice di caching resta in piedi e funzionante — per riattivarlo serve installare Redis e rimettere il flag a `true`. Da valutare: il caching riduce le chiamate API durante la sync notturna, ma richiede di mantenere un servizio in più
- **OpenTelemetry usa il Console exporter**: il tracing è attivo ma i trace finiscono a console, senza backend di raccolta (Jaeger/Tempo/Application Insights). Va bene per il debug, non per l'analisi storica. La Fase 2 (Health Checks UI popolata + backend di tracing) è predisposta ma non configurata
- Copertura limitata del backfill Tag su OrderItems storici (molti `CardTraderId` non trovano corrispondenza nei Blueprints locali)
- Applicare le migration manuali su server di produzione (SQL diretto: vedi sezione Punti di Attenzione)

---

## Decisioni Recenti

| Data | Decisione | Perché |
|------|-----------|--------|
| 2026-08-27 | `Redis:Enabled` → `false` | Nessun server Redis installato sulla macchina, ma il flag era `true`: ogni health check restava appeso 15s sul connect TCP. Config allineata alla realtà; il codice di caching resta intatto e riattivabile |
| 2026-08-27 | Il fallimento di Card Trader nell'health check produce `Degraded`, non `Unhealthy` | Un'API di terze parti lenta o giù non significa che la nostra applicazione sia down; con `Unhealthy` l'endpoint restituiva 503 e una liveness probe ci avrebbe riavviati in loop per un problema non nostro |
| 2026-08-27 | Per girare in locale si usa `ASPNETCORE_ENVIRONMENT=Development`, non si commenta il wiring | Commentare `UseWindowsService`/`UseUrls` per test ha lasciato la produzione senza servizio e senza backup per due giorni. L'environment Development ha già porta e config separate |
| 2026-05-22 | `NormalizeLanguageCode` nel mapper + endpoint `by-blueprint` | CT restituisce codici brevi (`"en"`, `"it"`); il mapper li converte a nomi completi per le future sync; il DTO li normalizza anche per i record già in DB, così la combo lingua nel form si popola correttamente |
| 2026-05-22 | Tag recuperato da `PendingListing` sincronizzata come fallback in `by-blueprint` | Gli `InventoryItem` da sync notturna CT non portano il tag utente (CT non lo espone); il fallback `ii.Tag ?? syncedPending?.Tag` lo recupera senza richiedere re-sync |
| 2026-05-20 | `domLayout: 'autoHeight'` rimosso da Items to Prepare | Causava rendering AG Grid a 952px (altezza naturale 25 righe) ignorando il contenitore CSS; la paginazione risultava tagliata da `overflow: hidden` |
| 2026-05-20 | Griglia Espansioni con layout flex (`flex: 1; min-height: 0`) | Sostituisce altezza fissa `600px`; la griglia ora riempie lo spazio disponibile coerentemente con Items to Prepare |
| 2026-05-20 | Sidenav container: `calc(100vh - 112px)` | Contabilizza sia toolbar (64px) che tab-bar (48px); prima tagliava il contenuto con il solo 64px |
| 2026-05-19 | `BoxPrice` salvato a DB insieme a PacksPerBox/CardsPerPack | Permette tracking del prezzo inserito e calcolo ROI Box% come colonna filtrabile in griglia |
| 2026-05-19 | `BoxRoiPercentage` calcolato server-side nel controller, non persistito | Dato derivato da campi già in DB; si ricalcola sempre aggiornato ad ogni fetch |
| 2026-05-19 | Migrazioni applicate via SQL diretto + snapshot aggiornato manualmente | API + VS lockano le DLL Infrastructure; workaround stabile: SQL diretto + `__EFMigrationsHistory` + snapshot |
| 2026-05-19 | Widget "Espansioni più Convenienti" rimosso dalla dashboard | Mostrava sempre le stesse espansioni vecchie; la colonna ROI Box% nella griglia Espansioni è più utile e sempre aggiornata |
| 2026-05-19 | Componente `profitability-analysis` rimosso | Usava `AVG(PurchasePrice)` da InventoryItems come proxy costo — inaffidabile; la redditività reale è in `ExpansionsROI` view |
| 2026-05-19 | `tag-profitability` spostato come tab nella Dashboard | Non necessita di voce di menù separata; il drill-down per tag è più accessibile nella dashboard principale |
| 2026-05-19 | Sync UPDATE aggiorna anche `InventoryItem` locale oltre a CT | Il pannello "Le mie inserzioni" legge da `InventoryItems`; senza aggiornamento locale i valori restano obsoleti fino alla sync notturna |
| 2026-05-19 | `PendingListing.IsUpdate` come flag per distinguere CREATE vs UPDATE su CT | Evita duplicazione di inserzioni quando si modifica una carta già listata su CT |
| 2026-03-27 | `TotaleAcquistato` nel report Tag usa JOIN diretto con `PendingListings` | Eliminare la query con `OPENJSON` che causava timeout 30s |
| 2026-03-27 | `ValoreRimanente` usa `InventoryItems.ListingPrice` (non `PurchasePrice`) | `PurchasePrice` sugli InventoryItems è spesso zero; il prezzo di mercato attuale è `ListingPrice` |
| 2026-08-20 | `Blueprint.ItalianName` + search per collector_number | Aggiunto campo `ItalianName` (lazy-popolato via Scryfall `localized.it` durante sync blueprint); esteso `SearchByNameAsync` per matchare anche `collector_number` (JSON) e `ItalianName`; autocomplete mostra nome italiano quando disponibile |
| 2026-08-24 | Redis caching per dati statici Card Trader (Games/Expansions/Blueprints/Categories) | Riduce drasticamente le chiamate API durante sync notturna e operazioni frontend; TTL configurabili via appsettings (Games 24h, Expansions 12h, Blueprints 6h); fallback trasparente se Redis non disponibile |
| 2026-08-24 | Health check endpoint `/health` con controlli DB, Card Trader API, Redis | Liveness/readiness probes per orchestrazione container/Kubernetes; DB via `SELECT 1`, CT via cached games (lightweight), Redis via test set/get/remove; Redis disabilitato in config = healthy (not required) |
| 2026-08-24 | Fix disallineamento `TotaleAcquistato` Tag vs Espansione | Query `GetTagExpansionProfitability` ora usa join espliciti (`from pl join bp join ex`) coerenti con `rimanentePerExpansion`; prima usava navigation properties con INNER JOIN implicito che escludeva record con Blueprint/Expansion NULL, mentre query Tag li includeva |

---

## Punti di Attenzione

- **Migrazioni manuali**: `20260519120000_AddBoxConfigToExpansions` e `20260519130000_AddBoxPriceToExpansions` applicate via SQL diretto (non `dotnet ef database update`). Registrate in `__EFMigrationsHistory`, snapshot aggiornato. Non hanno `.Designer.cs`. Il prossimo `migrations add` funzionerà correttamente.
- In produzione applicare le migration tramite SQL diretto (stessa procedura): `ALTER TABLE Expansions ADD PacksPerBox int NULL, CardsPerPack int NULL, BoxPrice decimal(18,2) NULL`
- La migration EF ufficiale più recente è `20260519073801_AddIsUpdateToPendingListings`
- **Nuova migration `20260820000000_AddItalianNameToBlueprints`**: aggiunge colonna `ItalianName nvarchar(max) NULL` a `Blueprints`. Applicata via SQL diretto (vedi `Migrazioni/20260820000000_AddItalianNameToBlueprints.sql`). Snapshot aggiornato. Non ha `.Designer.cs`.
- **Nuova migration `20260820202633_AddDescriptionToEntities`**: aggiunge `Description nvarchar(max) NULL` a `PendingListings` e `InventoryItems`; rimuove `Location` da `PendingListings` (sostituito da Description nel form "Nuovo Prodotto"). Applicata via `dotnet ef database update` in dev; in produzione applicare via SQL: `ALTER TABLE PendingListings DROP COLUMN Location; ALTER TABLE PendingListings ADD Description nvarchar(max) NULL; ALTER TABLE InventoryItems ADD Description nvarchar(max) NULL`. Snapshot aggiornato.
- **Popolamento lazy `ItalianName`**: la prima sync blueprint completa farà molte chiamate a Scryfall (rate-limit 100ms/call lato client); le sync successive toccano solo i blueprint senza nome italiano. Scryfall espone `localized.it` solo per carte che hanno una versione italiana stampata.
- Il backfill Tag (`POST /api/cardtrader/orders/backfill-tags`) ha copertura parziale
- Il file `debug_expansion_{id}.csv` viene generato da `ExpansionAnalyticsService` — non committare
- L'AI Grading usa un mock service: Ximilar richiede abbonamento a pagamento
- Il seed crea sempre un utente `admin` — la logica controlla che non duplichi
- **⚠️ L'autopricer nasce in dry-run**: il profilo predefinito ha `DryRun = true` e non modifica alcun prezzo. Si attiva dall'interruttore nella scheda Regole, o via `PUT /api/pricing/profiles/{id}`. In produzione `AutoPricing:Enabled` e `AutoPricing:RepriceOnOrder` sono `true`, quindi il worker gira: finché il profilo è in dry-run calcola e registra senza scrivere
- **⚠️ Migration con colonne bool**: EF le aggiunge con default `false`, quindi un profilo già a database non eredita il default definito in C#. Dopo una migration di questo tipo serve un `UPDATE` esplicito sulle righe esistenti
- **⚠️ I servizi one-shot chiudono il processo**: `PopulateItalianNamesService` e `SealedProductPriceService` terminano con `Environment.Exit(0)`. La guard clause sul flag precede l'`Exit`, quindi con flag `false` sono innocui — ma **non vanno mai abilitati in `appsettings.Production.json`**, pena lo spegnimento del servizio Windows a ogni avvio. Vanno lanciati on-demand dagli endpoint dedicati (es. `POST /api/expansions/sync-sealed-prices`)
- **⚠️ Non commentare il wiring di produzione in `Program.cs` per test locali**: `UseWindowsService`, `UseUrls(apiBaseUrl)` e la registrazione di `BackupService`/`ScheduledProductSyncWorker` sono ciò che tiene in piedi il deploy. Per girare in locale si usa `ASPNETCORE_ENVIRONMENT=Development` (porta 5155 da `appsettings.Development.json`), che non richiede di toccare il codice
- **Redis caching**: richiede istanza Redis disponibile (default `localhost:6379`); se non raggiungibile, il servizio disabilita il caching silenziosamente e continua senza cache (graceful degradation). Configurabile via `Redis:Enabled=false` o `Redis:ConnectionString`.

---

## Backlog Tecnico (Punto Aperto)

_Nessun punto aperto al momento._

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
| `eCommerce.Inventory.Api/Controllers/ExpansionsController.cs` | Gestione espansioni + calcolatore box (BoxConfigDto, BoxRoiPercentage) |
| `eCommerce.Inventory.Api/HealthChecks/` | Health check endpoint `/health` (DatabaseHealthCheck, CardTraderApiHealthCheck, RedisHealthCheck) |
| `eCommerce.Inventory.Api/appsettings.json` | Configurazione (senza segreti) |
| `publish.ps1` | Script deploy automatizzato (richiede permessi Admin) |
| `ecommerce-inventory-ui/src/app/shared/components/blueprint-selector/blueprint-selector.component.ts` | Selector carta in "Nuovo Prodotto" (autocomplete con nome IT) |
| `ecommerce-inventory-ui/src/app/features/expansions/pages/expansions-page.component.ts` | Pagina espansioni con calcolatore box |
| `ecommerce-inventory-ui/src/app/features/inventory/pages/dashboard/` | Dashboard principale (2 tab) |
| `ecommerce-inventory-ui/src/app/features/reporting/pages/` | Report Vendite, Inventario, Tag |

---

## Ambiente di Sviluppo

| Voce | Valore |
|------|--------|
| Database | `ECommerceInventory` su `.\SQLEXPRESS` |
| Backend (dev) | `http://localhost:5155` |
| Frontend (prod) | `http://inventory.local` (IIS) |
| Frontend (dev) | `http://localhost:4200` |
| Log backend | `Publish/api/logs/` |
