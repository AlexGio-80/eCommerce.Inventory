# eCommerce.Inventory - Changelog

Questo file traccia le principali feature e bug fix implementate nel progetto, con dettagli tecnici per facilitare future analisi e manutenzione.

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
