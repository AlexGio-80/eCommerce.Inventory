# eCommerce.Inventory - Changelog

> Una voce per ogni sessione di lavoro significativa. Le voci più recenti vanno in cima.
> Formato sezioni: **Problema** (cosa non funzionava), **Soluzione** (cosa è cambiato), **Note Tecniche** (dettagli per manutenzione futura).

---

## [Unreleased]

> Modifiche in corso, non ancora in produzione.

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
