# eCommerce.Inventory - Changelog

Questo file traccia le principali feature e bug fix implementate nel progetto, con dettagli tecnici per facilitare future analisi e manutenzione.

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
