# eCommerce.Inventory - Changelog

Questo file traccia le principali feature e bug fix implementate nel progetto, con dettagli tecnici per facilitare future analisi e manutenzione.

---

## [2026-02-06] Create Listing - Prezzi Suggeriti Filtrati

### Problema
I prezzi suggeriti (Min, Medio, Max) nella maschera di creazione prodotto erano generici per il Blueprint, senza considerare condition, lingua o flag foil/signed, portando a suggerimenti poco accurati. Inoltre, inserzioni placeholder con prezzi esorbitanti falsavano le statistiche.

### Soluzione Implementata

**`CardTraderInventoryController.cs`**
- Endpoint `GetMarketplaceStats` ora supporta filtri opzionali: `condition`, `language`, `isFoil`, `isSigned`.
- Implementato tetto massimo di **1000€** per ignorare inserzioni "preliminari" o fuori mercato.

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
