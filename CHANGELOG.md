# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed
- **L'autopricer si posiziona per percentile e non più per numero d'ordine**: nuova modalità `PercentileOffer` ("collocati al N% della scaletta comparabile"). Le offerte comparabili misurate sulle carte reali vanno da 3 a 29, e con un ordinale fisso la stessa regola significava "stai in fondo" su un mercato profondo e "sii il più caro" su uno sottile. Regole convertite a 15% sul bulk, 20% fra 1 e 25 €, 40% sopra i 25 €
- **Guardrail sdoppiato per direzione**: `MaxChangePercentPerRun` diventa `MaxIncreasePercentPerRun` (300%) e `MaxDecreasePercentPerRun` (25%). Un rialzo eccessivo lascia la carta invenduta e si corregge da solo, un ribasso eccessivo la fa svendere e non si recupera. Migration `PercentileEGuardrailAsimmetrico`
- **La vendita scala subito la giacenza locale**: il webhook `order.create` sottrae le quantità vendute per `product_id`, con guardia di idempotenza sui webhook duplicati. Prima l'inventario mostrava carte già vendute fino alla sincronizzazione notturna, e la rivalutazione immediata sprecava chiamate al marketplace su carte esaurite

### Fixed
- **L'autopricer confrontava il prezzo venditore con i prezzi acquirente del marketplace**: `ListingPrice` viene dall'export ed è quello che si incassa, mentre le offerte del marketplace includono il sovrapprezzo di Card Trader — la propria inserzione compare nel feed a un valore più alto di quello impostato. Il motore si credeva più economico di quanto fosse e proponeva rialzi anche quando la posizione era già corretta. Ora ricava il fattore di conversione dalla propria offerta nel feed, senza dover conoscere la formula della commissione
- **Il riferimento poteva cadere sull'offerta più cara del mercato**: succedeva in 4 casi su 11 sulle carte reali, e in uno di questi la più cara era un prezzo di comodo da 1019 € su un mercato di 73–96 €. Ora il riferimento non può mai coincidere con il massimo
- **Lo scarto delle offerte anomale non girava sui mercati sottili**: `MinOffersForOutlierRejection` era 5, cioè disattivato proprio con 3-4 offerte, dove un singolo prezzo di comodo arriva dritto al riferimento. Soglia abbassata a 3 e affiancata da un filtro di rapporto sulla mediana sempre attivo, che intercetta anche i prezzi irrealistici dei venditori inesperti
- **I log di produzione finivano in `C:\Windows\System32`**: un servizio Windows eredita quella cartella corrente, quindi il percorso relativo del sink veniva risolto lì, dove l'account del servizio non può scrivere. `Program.cs` allinea ora la cartella corrente a quella dell'eseguibile, correggendo tutti i percorsi relativi. Causa individuata grazie al `SelfLog` di Serilog
- **`publish.ps1` non concedeva i permessi a NetworkService**: usava il nome `NT AUTHORITY\NETWORK SERVICE`, che è localizzato e non si risolve su Windows italiano, con l'errore ingoiato tre volte. Ora usa il SID `*S-1-5-20` e verifica l'exit code
- **Rimossi dalla configurazione Serilog gli enricher `WithThreadId` e `WithProcessId`**, che richiedono pacchetti non referenziati ed erano ignorati in silenzio
- **La sincronizzazione dell'inventario era ferma da dicembre 2025 senza segnalarlo**: `UpsertInventoryAsync` costruiva il lookup di `Tag` e `PurchasePrice` da `PendingListings` con `ToDictionaryAsync`, che solleva un'eccezione sulla chiave duplicata. Lo stesso `CardTraderProductId` compare su più `PendingListings` (550 casi, il primo del 03/12/2025), e l'eccezione arrivava prima del ciclo di upsert: ogni notte la sezione inventario abortiva senza inserire né cancellare nulla. Deriva accumulata: 282 articoli nel DB non più su Card Trader, 192 carte Magic mancanti, 203 quantità disallineate. Il lookup ora raggruppa e tiene la registrazione più recente
- **I fallimenti parziali di sincronizzazione venivano riportati come successo**: `SyncInventoryAsync` valorizzava `response.Inventory.ErrorMessage` ma non `response.ErrorMessage`, e `ScheduledProductSyncWorker` decide l'esito solo su quest'ultimo. Una sezione poteva fallire in blocco e l'esecuzione risultava `success` sia nel log sia nella metrica `ecommerce_sync_total`. `SyncAsync` ora raccoglie le sezioni fallite e le propaga sull'esito complessivo, con un log a livello `Error`
- **In produzione non veniva scritto alcun log**: `MinimumLevel` era `Warning` e il sink su file non crea il file finché non si verifica un evento di quel livello, quindi `Publish/api/logs` restava vuota e i riepiloghi di sync e autopricer (livello `Information`) sparivano. Ora `Information` con `Override` su `Microsoft`, `Microsoft.EntityFrameworkCore` e `System`, più `retainedFileCountLimit: 14`
- **Doppio sink File sullo stesso percorso**: gli array di configurazione in .NET si fondono per indice e non si concatenano, quindi il sink dichiarato in `appsettings.json` più quello dichiarato nel file per ambiente ne producevano due sullo stesso file, di cui il secondo non riusciva a prendere il lock (origine dei file con suffisso `_001`). Il sink File resta ora solo nei file per ambiente
- **Lo storico prezzi delle carte vendute veniva cancellato**: `FK_PriceChangeLogs_InventoryItems_InventoryItemId` era in `CASCADE`, quindi la rimozione di una carta venduta si portava via anche le sue valutazioni — proprio quelle su cui conviene verificare se il prezzo proposto era corretto (83 righe su 4.799 dell'esecuzione del 28/08). Foreign key passata a `SET NULL` con `PriceChangeLog.InventoryItemId` nullable (migration `20260828071742_PreservaStoricoPrezziCarteVendute`); la carta resta identificabile da `BlueprintId`

### Added
- **Dettaglio carta per carta delle esecuzioni dell'autopricer**: nella scheda Storico la riga di un'esecuzione apre la griglia dei calcoli — carta, prezzo attuale, proposto, variazione, offerte comparabili, anomale scartate, esito e motivazione. Filtro per esito lato server e colonna "Magazzino" che distingue le valutazioni di carte non più a magazzino. `GET /api/pricing/runs/{id}/changes` accetta ora `outcome` e restituisce `{ totalCount, returnedCount, items }`
- **Autopricer custom**: motore di pricing a regole in alternativa all'autopricer nativo di Card Trader
  - `PricingEngine` — logica pura senza dipendenze da rete o database, 21 test. Esclude le proprie offerte, filtra per comparabilità reale (condizione/lingua/foil con normalizzazione `en`↔`English`), scarta gli outlier con MAD, applica regola di fascia e guardrail
  - **Scarto outlier al posto del filtro recensioni**: l'API Card Trader non espone il feedback dei venditori (verificato su offerte reali). Disponibili solo `user_type`, `country_code`, `max_sellable_in24h_quantity` e `on_vacation`
  - **Copertura a rotazione**: carte di valore ogni notte + fetta di bulk scelta per anzianità di valutazione. Un giro completo dei ~19.000 blueprint richiederebbe 16 ore a 20 req/min
  - **Riallineamento prezzi da Card Trader** prima di ogni esecuzione, con una sola chiamata all'endpoint di export
  - **Salto su mercato sottile**: una regola posizionale non viene applicata se i venditori comparabili sono meno della posizione richiesta, per non allinearsi all'offerta più cara e non innescare rincorse al rialzo fra autopricer
  - **Reprice alla vendita**: il webhook `order.create` accoda i blueprint venduti e risponde subito; un worker consuma la coda in background
  - **Dry-run come modalità del profilo**, con guardrail su prezzo minimo e variazione massima per esecuzione
  - **Storico e copertura** di ogni valutazione, applicata o meno, con il motivo
  - Interfaccia `/layout/pricing` con schede Regole, Anteprima, Copertura, Storico
  - 4 tabelle nuove (migration `AddAutoPricing` + `AddSkipWhenFewerOffersThanPosition`), endpoint `/api/pricing`
- **Monitoring/Observability — Fase 1 Core (Prometheus + OpenTelemetry + Correlation ID + Serilog Config)**: Layer completo di metriche, tracing distribuito e correlation ID per produzione
  - **Prometheus metrics endpoint** (`/metrics`) con metriche runtime (`dotnet_*`), HTTP (`http_requests_*`), e **metriche business custom** (20+ metriche):
    - `ecommerce_sync_duration_seconds` (istogramma durata sync), `ecommerce_sync_success_total`/`ecommerce_sync_failure_total` (contatori)
    - `ecommerce_orders_created_total`, `ecommerce_inventory_items_total` (gauge), `ecommerce_active_users_total` (SignalR)
    - `ecommerce_api_calls_total`, `ecommerce_webhook_received_total`, `ecommerce_db_query_duration_seconds`, `ecommerce_cache_hits_total`/`ecommerce_cache_misses_total`
    - `ecommerce_background_job_duration_seconds`, `ecommerce_auth_attempts_total`
  - **OpenTelemetry distributed tracing** per ASP.NET Core, HttpClient (Card Trader, Scryfall), EF Core con **Console Exporter** per sviluppo
  - **Correlation ID middleware** propagazione header `X-Correlation-ID` con enrichment Serilog LogContext (`CorrelationId`, `TraceId`, `SpanId`)
  - **Serilog configurazione da appsettings.json** (environment-specific): Development (Debug + Console + File), Production (Warning + File only, Enrich FromLogContext/ThreadId/ProcessId)
  - **Health Checks** con UI preparation (`AspNetCore.HealthChecks.UI` + InMemory storage) su `/health` (JSON detailed) e `/health-ui`
  - **Rate Limiting** policies: API (100/min), CardTrader Sync (10/min), Auth (5/min sliding), Global fallback (200/min)
  - **CORS** configurato per frontend (`localhost:4200`, `inventory.local`)
- **Sealed Product Sync — Prezzi box automatici nel calcolatore espansioni**: background service one-shot che recupera i prezzi dei prodotti sigillati (booster box, case, starter deck) da Card Trader marketplace e popola automaticamente `Expansion.BoxPrice`
  - Nuovo background service `SealedProductPriceService` (abilitato via `SyncSettings:PopulateSealedPricesOnStartup=true`)
  - Identificazione prodotti sigillati tramite `Blueprint.CategoryId` mappati a categorie "sealed" note per gioco (MTG: 4,5,7,10,13; Force of Will: 30,31,33,34; Pokémon: 4576,4580; Lorcana: 12821,12825)
  - Nuova classe dominio `SealedCategoryIds` con metodo `IsSealedCategory(gameId, categoryId)`
  - Proprietà calcolata `Blueprint.IsSealedProduct` per check rapido
  - Logica pricing: per ogni espansione, chiama `GetMarketplaceProductsByExpansionAsync`, filtra per categoria sealed + lingua English, prende i 10 prezzi minimi, calcola media → `BoxPrice` in euro
  - Rate limiting integrato (20 req/min) con delay 500ms tra espansioni
  - Esecuzione one-shot: processa, popola, esce (`Environment.Exit(0)`) — **non incluso nella sync notturna**, da lanciare manualmente on-demand (settimanale consigliato) via endpoint o config startup
  - Endpoint manuale `POST /api/expansions/sync-sealed-prices` per trigger on-demand da UI
  - Frontend: `expansions.service.ts` aggiunge `syncSealedPrices()` method
- **UI: Aumentate dimensioni immagini nel blueprint-selector dropdown** (maschera "Nuovo Prodotto")
  - Immagine card: 30×42px → 50×70px
  - Gap: 12px → 16px, Padding: 4px → 8px
  - min-height option: 56px → 96px
  - Font size/nome aumentati, aggiunto box-shadow
  - Panel autocomplete min-width: 400px
- **Popolamento nomi italiani da MTGJSON**: servizio one-shot per popolare `Blueprint.ItalianName` su tutto il database
  - Nuovo background service `PopulateItalianNamesService` (abilitato via `SyncSettings:PopulateItalianNamesOnStartup=true`)
  - Integrazione **MTGJSON AllPrintings.json** come fonte primaria (copertura ~95%+ vs ~0.1% Scryfall)
  - Match preciso su `identifiers.scryfallId` ↔ `Blueprint.ScryfallId` (ID specifico per stampa)
  - Fallback automatico su Scryfall API per carte non coperte da MTGJSON
  - Batch processing (500 record/batch) con logging dettagliato progresso
  - Esecuzione one-shot: scarica, popola, esce (`Environment.Exit(0)`)
- **Calcolatore Box su pagina Espansioni**: pannello interattivo per calcolare la convenienza di acquistare un box sigillato
  - Campi `PacksPerBox`, `CardsPerPack`, `BoxPrice` salvati a DB (migration `20260519120000` + `20260519130000`)
  - Endpoint `PATCH /api/expansions/{id}/box-config` aggiornato per salvare anche `BoxPrice`
  - `BoxRoiPercentage` calcolato server-side: `(avgCardValue × cardsPerBox - boxPrice) / boxPrice × 100`
  - Colonna **ROI Box%** in griglia Espansioni: verde >20%, arancio 0-20%, rosso <0%; filtrabile e ordinabile
  - Valori pre-caricati dalla selezione riga; "Salva config" persiste tutti e tre i valori
- **Dashboard tab "Redditività per Tag"**: il componente `tag-profitability` è ora un tab nella dashboard principale invece di una pagina separata
- **Ricerca blueprint per Collector Number e Nome Italiano** nel selector della maschera "Nuovo Prodotto"
  - `Blueprint.ItalianName` salvato a DB (migration `20260820000000_AddItalianNameToBlueprints`); popolato lazy durante la sync blueprint via Scryfall `localized.it`
  - `SearchByNameAsync` ora matcha anche su `collector_number` (in `FixedProperties` JSON) e su `ItalianName`
  - Il selector mostra il nome italiano sotto a quello inglese quando disponibile
- **Campo Descrizione Card Trader** nella maschera "Nuovo Prodotto": scrittura e lettura del campo `description` di Card Trader (es. "Timbro dei nazionali Italiani"), sostituisce il campo "Posizione" nel form
  - `GET /api/v2/products/{id}` per leggere la descrizione esistente (`GetProductDetailAsync`)
  - Payload CREATE/UPDATE su CT ora include `description` (`CardTraderApiClient`)
  - `Description` su `PendingListing` e `InventoryItem` (migration `20260820202633_AddDescriptionToEntities`)
  - Form UI: "Posizione" → "Descrizione" con hint "Descrizione visibile su Card Trader"

### Fixed
- **Ripristino wiring di produzione disattivato durante il debug del monitoring**: in `Program.cs` erano rimasti commentati `UseWindowsService` (servizio Windows non avviabile), `UseUrls(apiBaseUrl)` (API non in ascolto su 5152), `ScheduledProductSyncWorker`, `BackupService` + `Configure<BackupSettings>` (nessun backup giornaliero), `PopulateItalianNamesService` e `SealedProductPriceService`. Tutti riattivati
- **`/health` bloccato 15,4s con HTTP 503**: `Redis:Enabled` era `true` senza un server Redis installato, quindi ogni probe restava appeso sul connect TCP e consumava anche il timeout del check Card Trader. `Redis:Enabled` → `false` (codice di caching intatto, riattivabile installando Redis). Ora `/health` risponde in 0,33s con HTTP 200
- **`CardTraderApiHealthCheck` restituiva `Unhealthy` per problemi esterni**: un'API di terze parti lenta o irraggiungibile marcava l'intera applicazione come down (503 → rischio riavvii in loop). Ora restituisce `Degraded`, con il timeout distinto dagli altri errori
- **App in dev si spegneva da sola dopo l'avvio**: `PopulateSealedPricesOnStartup` era `true` in `appsettings.Development.json` e `SealedProductPriceService` termina con `Environment.Exit(0)`. Riportato a `false` (resta lanciabile on-demand)
- **Rimossi residui di debug**: endpoint `/test-debug`, `/test-minimal`, `/test-health`, i `Log.Information` di tracciamento nella pipeline, e dalla root i file spuri `Program.cs` (Hello World), `stop` e la cartella con path preso alla lettera
- **PurchasePrice perso dopo sync notturna**: il mapper `MapProductToInventoryItem` ora accetta un `purchasePrice` opzionale da `PendingListing` e lo propaga sull'`InventoryItem` creato dalla sync; sia `InventorySyncService.SyncProductsAsync` che `CardTraderSyncOrchestrator.UpsertInventoryAsync` fanno lookup del `PurchasePrice` (e `Tag`) da `PendingListing` per il `CardTraderProductId` corrispondente. Risolve il problema per cui il giorno dopo la sync il campo `PurchasePrice` risultava vuoto nel pannello "Le mie inserzioni".

### Changed
- **Report Inventario** (`/report/inventory`): rielaborato per usabilità
  - Soglia "slow movers" configurabile con input giorni + pulsante "Cerca"
  - Griglia AG Grid con sort/filter su tutte le colonne, stato persistente, default sort desc per `daysInInventory`
  - 4 KPI: Valore Totale, Totale Articoli, Prodotti Unici, Valore Medio Articolo
  - Hint descrittivo con conteggio articoli trovati
  - Fix: query EF Core per slow movers riscritta in 2 step (fetch DB + proiezione in memoria) per evitare `InvalidOperationException` su `TimeSpan.TotalDays`
- **Report Vendite** (`/report/sales`): rielaborato per usabilità
  - Filtro date (Dal/Al) con default ultimi 30 giorni; raggruppamento automatico giorno/settimana/mese
  - Griglia top prodotti con AG Grid: 20 items, colonne sortabili/filtrabili, stato persistente
  - Crescita % con segno +/- e colori verde/rosso
- **Dashboard — widget "Ultimo Sync"**: ora legge `lastSyncTime` da `localStorage` (valorizzato dalla sync page ad ogni sync completata); mostra `—` se mai sincronizzato

### Removed
- **Componente `profitability-analysis`** e relativa route: rimosso per dati inaffidabili (`AVG(PurchasePrice)` come proxy costo non rappresentativo)
- **Widget "Espansioni più Convenienti"** dalla dashboard: mostrava dati stantii non aggiornati regolarmente
- **Voce di menù "Redditività per Tag"** dal sidenav: accessibile come tab nella dashboard

### Fixed
- **Form "Nuovo Prodotto" — lingua non selezionata**: CT restituisce codici brevi (`"en"`, `"it"`) nella `properties_hash`; il mapper ora li normalizza a nomi completi (`"English"`, `"Italian"`) tramite `NormalizeLanguageCode`; l'endpoint `by-blueprint` applica la stessa normalizzazione ai record esistenti in DB, così la combo si popola correttamente
- **Form "Nuovo Prodotto" — tag non recuperato**: gli `InventoryItem` creati dalla sync notturna CT non portano il tag utente (CT non lo espone); l'endpoint `by-blueprint` ora recupera il tag dal `PendingListing` sincronizzato corrispondente come fallback su `InventoryItem.Tag == null`
- **Struttura template HTML** di `expansions-page.component.ts`: rimosso `</div>` extra che chiudeva prematuramente `expansion-dashboard`, il calcolatore box ora è correttamente dentro la grid CSS
- **TypeScript build error** su `cellStyle` AG Grid: sostituito `return {}` con `return null` per compatibilità con `CellStyle | null | undefined`
- **ModuleRegistry.registerModules** rimosso da `expansions-page.component.ts` (già registrato globalmente in `app.config.ts`)
- **Griglia Items to Prepare — paginazione tagliata**: rimosso `domLayout: 'autoHeight'` da `gridOptions` (causava rendering a 952px ignorando il contenitore CSS di 458px); AG Grid ora in layout `normal`, stati loading/empty gestiti con `showLoadingOverlay()` / `showNoRowsOverlay()`; rimosso `*ngIf` dal container per garantire inizializzazione con dimensioni corrette
- **Griglia Espansioni — altezza fissa 600px**: sostituita con layout flex (`flex: 1; min-height: 0`) sulla card e sul componente AG Grid; la griglia ora riempie tutto lo spazio disponibile come Items to Prepare
- **Sidenav container overflow**: altezza corretta da `calc(100vh - 64px)` a `calc(100vh - 112px)` (64px toolbar + 48px tab-bar)

---

## [1.1.0] - 2026-05-19

### Added
- **Pannello "Le mie inserzioni"** in Nuovo Prodotto: mostra inserzioni esistenti su CT per la carta selezionata, permette di pre-compilare il form e inviarle come UPDATE (non nuova inserzione)
- **Flag `IsUpdate`** su `PendingListing`: distingue operazioni CREATE vs UPDATE su Card Trader API (migration `20260519073801_AddIsUpdateToPendingListings`)
- **Aggiornamento `InventoryItem` locale** durante sync UPDATE: il sync su CT ora aggiorna anche il record locale per evitare disallineamenti visibili nel pannello inserzioni
- **Endpoint `POST /api/cardtrader/orders/backfill-tags`**: assegna retroattivamente i Tag agli OrderItems storici basandosi sul `CardTraderId`

### Fixed
- **Report Redditività per Tag**: query `TotaleAcquistato` riscritta con JOIN diretto su `PendingListings` (eliminato `OPENJSON` che causava timeout 30s)
- **ValoreRimanente nel report Tag**: usa `InventoryItems.ListingPrice` invece di `PurchasePrice` (spesso zero)

---

## [1.0.0] - 2025-11-28

### Added
- **Phase 6: Rate Limiting & Backup System**
  - Outbound rate limiting for Card Trader API (20 requests/minute)
  - Comprehensive daily backup system (database + application files)
  - Configurable backup schedule and retention policy
- **Phase 5: Deployment**
  - Windows Service deployment for backend API
  - IIS hosting for Angular frontend
  - Automated deployment scripts (`publish.ps1`, `setup-iis.ps1`)

## [0.9.0] - 2025-11-27

### Added
- **Phase 3.12: Authentication & Security**
  - JWT Bearer Token authentication
  - User entity with BCrypt password hashing
  - Login/logout functionality
  - AuthGuard for route protection
  - AuthInterceptor for automatic token attachment

### Fixed
- Admin user seeding logic (independent check for admin username)
- CORS configuration for authentication support
- API URL port correction (5155 → 5152)

## [0.8.0] - 2025-11-25

### Added
- **Phase 3.11: Reporting & Analytics System**
  - Sales Dashboard (revenue, orders, AOV, growth metrics)
  - Inventory Analytics (value distribution, slow-movers)
  - Profitability Analysis (profit margins, ROI, top performers)
  - 10 reporting endpoints with Chart.js visualizations
  - AG-Grid integration for data tables

### Fixed
- Property name mismatches in reporting DTOs
- API URL correction (localhost:5000 → localhost:5152)
- Safe navigation operators in templates
- Blueprint search field overlapping text
- Pending listings display issue (response.data.items unwrapping)

## [0.7.0] - 2025-11-24

### Added
- **Phase 3.8-3.10: Orders Grid Enhancements**
  - Multi-column sorting (Shift+Click)
  - Grid state persistence (column dimensions, order, visibility, sort)
  - Visual improvements (colored badges, flag icons, foil star icon)
  - Card Trader integration (opens product page when marking items as prepared)
  - Auto-sync for unprepared items (every 5 minutes when tab is active)

### Fixed
- Missing form fields in product listing UI
- Price suggestions (Local Blueprint ID → Card Trader Blueprint ID)
- JSON deserialization for marketplace products
- Tag sync payload structure
- Dashboard N+1 query issue (added AsNoTracking)
- Grid loading issue on first visit

## [0.6.0] - 2025-11-23

### Added
- **Phase 4: API Controller Enhancement**
  - `ApiResponse<T>` generic response envelope
  - `PagedResponse<T>` for pagination
  - GlobalExceptionMiddleware for centralized error handling
  - Server-side filtering for inventory (card name, expansion, condition, language)

### Changed
- Migrated CardTraderInventoryController and CardTraderOrdersController to new response format
- Frontend service updated to unwrap ApiResponse envelope

### Fixed
- Massive performance issue (removed virtual keyword from InventoryItem.OrderItems)
- Empty inventory grid (response unwrapping)

## [0.5.0] - 2025-11-22

### Added
- **Phase 3.5: Advanced Grid Features**
  - AG-Grid standardization across all data grids
  - Column visibility menu with Material Design
  - Grid state persistence (manual save)
  - Server-side pagination for inventory
  - Infinite scroll support

### Changed
- Converted all mat-table grids to AG-Grid
- Optimized "To Prepare" list with UnpreparedItemDto
- Added OrderItem-Blueprint relationship

### Fixed
- TypeScript compilation error (rowModelType)
- Database migration constraint FK violation
- Performance issue (removed domLayout: 'autoHeight')

## [0.4.0] - 2025-11-21

### Added
- **Phase 3.3-3.4: Product Listing & Webhook Integration**
  - Pending listings system (queue-based workflow)
  - Price suggestions from Card Trader marketplace
  - SignalR real-time notifications
  - Webhook integration for live inventory updates

## [0.3.0] - 2025-11-20

### Added
- **Phase 3.1-3.2: Frontend Foundation**
  - Angular 20 project setup
  - Dashboard with KPI cards
  - Inventory list with Material table
  - Card Trader data initial sync
  - Games and Expansions management pages

## [0.2.0] - 2025-11-19

### Added
- **Phase 2: Backend Integration**
  - Card Trader API client with DTOs
  - Inventory sync service
  - Webhook processing (order.create, order.update, order.destroy)
  - WebhookSignatureVerificationService (HMAC SHA256)
  - MediatR command handling
  - Backend unit and integration tests (14 passing)

## [0.1.0] - 2025-11-18

### Added
- **Phase 1: Database & Architecture**
  - Clean Architecture (4 layers: Domain, Application, Infrastructure, API)
  - Entity Framework Core with SQL Server
  - Initial migration (Games, Expansions, Blueprints, InventoryItems, Orders, OrderItems)
  - Repository pattern
  - Serilog logging configuration
  - Seed data for testing

---

## Legend

- **Added**: New features
- **Changed**: Changes in existing functionality
- **Deprecated**: Soon-to-be removed features
- **Removed**: Removed features
- **Fixed**: Bug fixes
- **Security**: Security improvements
