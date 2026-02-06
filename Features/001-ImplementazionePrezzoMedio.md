## [2026-02-06] Miglioramento calcolo prezzi da proporre in inserimento carte

### Problema
Nella maschera di Create New Product Listing, abbiamo una sezione in cui vengono proposti il prezzo minimo, medio e massimo della carta che si sta inserendo. questi valori vengono calcolati prendendo tramite Api le inserzioni attualmente attive per la carta che si sta inserendo e facendono i vari calcoli. Vorrei implementare questa cosa applicando dei filtri ai valori presi in esame per proporre questi valori in base al Grading, lingua e ai flag Foil e Signed che applico durante la compilazione per l'inserimento. Quindi se seleziono un Grading "Near Mint", Language "English" e Foil "false" Signed "false" il prezzo minimo, medio e massimo devono essere calcolati usando questi dati come filtro. In oltre applicherei un filtro di prezzo inferiore ai 1000 euro, in quanto molti utenti caricano le carte con valori assurdi in attesa che gli algoritmi di aggiornamento dei prezzi che hanno impostato cambino questo valore al prezzo di mercato desiderato, ma se noi prendiamo in considerazione anche queste inserzioni "preliminari", potremmo chiamarle", il prezzo medio e massimo diventano sempre falsati perché non rispecchiano mai un valore realistico.

### Causa Radice
Analisi tecnica del problema.

### Soluzione Implementata
- **Backend (`CardTraderInventoryController.cs`)**: Modificato l'endpoint `GetMarketplaceStats` per accettare filtri `condition`, `language`, `isFoil`, `isSigned`.
- **Logica di Filtraggio**: Implementato filtro lato server sui prodotti del marketplace di Card Trader con supporto robusto per `JsonElement` e mapping flessibile per abbreviazioni (NM, SP, etc.) e codici lingua (en, it, etc.).
- **Price Cap**: Inserito un tetto massimo di **1000€ (100.000 cent)** per escludere inserzioni placeholder esorbitanti che falsificano la media.
- **Frontend Service (`cardtrader-api.service.ts`)**: Aggiornato `getMarketplaceStats` per inviare i parametri di filtraggio.
- **Frontend Component (`create-listing.component.ts`)**: 
  - Implementato listener `valueChanges` sul form con `debounceTime(500)` e `distinctUntilChanged`.
  - Aggiornamento automatico dei prezzi suggeriti ogni volta che cambia Condizione, Lingua, Foil o Signed.

### Verifica
- Build superato con successo.
- Test manuale: Variando i parametri nel form, la chiamata API verso `marketplace-stats` include correttamente i filtri e aggiorna i valori Min/Avg/Max proposti.
- Fix: Risolto problema di prezzi a zero dovuto alla mancata deserializzazione delle proprietà annidate nel JSON di Card Trader.

### Note Tecniche
- Il filtraggio viene effettuato in memoria sui prodotti recuperati per il Blueprint specifico.
- I nomi dei campi proprietà nell'API Card Trader sono stati mappati correttamente (`condition`, `language`, `mtg_language`, `mtg_foil`, `signed`).
- Utilizzati helper `GetStringFromHash` e `GetBoolFromHash` per estrarre valori da `JsonElement`.
