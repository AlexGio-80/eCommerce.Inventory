## [2026-03-26] Import Tag su dettaglio ordini
Leggi ..\Documentation\ARCHITECTURE.md per sapere le linee guida del progetto e come gestire le implementazioni

### Problema
Nella maschera di Items to Prepare, abbiamo implementato la griglia degli articoli da preparare per la spedizione su cui c'è una colonna Tag che però non mostra nessun valore o a volte mostra dei caratteri astrusi.
Visto che il Tag sulle carte che metto in vendita lo volevo usare anche per fare delle analisi andiamo a fare questi miglioramenti.
1) Nella tabella dbo.OrderItems dove salviamo i dettagli delle carte vendute, aggiungiamo una colonna Tag.
2) Quando invochiamo l'endpoint per recuperare le carte vendute (es. https://api.cardtrader.com/api/v2/orders/32055829) recuperiamo il valore del Tag associato al "order_items" e lo salviamo nella nuova colonna creata nella tabella dbo.OrderItems.
3) nella maschera di Items to Prepare, nella griglia visualizziamo nella colonna Tag il valore della nuova colonna Tag nella tabella dbo.OrderItems.
4) Creaiamo un nuovo report tra i "Report Redditività" dove analizziamo e visualizziamo il totale del valore di acquisto per Tag, il totale del venduto per Tag e il valore e percentuale di guadagno/perdita. In oltre dato che un tag può (anzi è sempre così) comprendere diverse espansioni visto che in una bustina posso trovare carte di espansioni e sotto-espansioni diverse, sarebbe interessante se su questa riga di analisi per Tag si potesse espanderla e vedere gli stessi valori calcolati divisi per singola espansione per avere un'analisi ancora più approfondita.

### Causa Radice
La colonna `Tag` non esisteva nella tabella `dbo.OrderItems`, quindi il valore non veniva persistito né letto correttamente. Il campo `tag` nella griglia Items to Prepare mostrava dati assenti o errati perché si leggeva da una colonna inesistente.

### Soluzione Implementata (2026-03-26)

**Backend:**
1. Aggiunta property `Tag` all'entità `OrderItem` (Domain layer)
2. Migration `20260326145049_AddTagToOrderItems` — aggiunge colonna `Tag` (nvarchar nullable) con backfill da `UserDataField`
3. `CardTraderDtoMapper.MapOrderItem()` — corretto mapping: `Tag = dto.Tag` (campo `tag` nell'endpoint dettaglio ordine) + `Price = (dto.SellerPrice?.Cents ?? dto.Price?.Cents ?? 0) / 100m`
4. `CardTraderOrderItemDto` — aggiunti `[JsonPropertyName("tag")] Tag`, `[JsonPropertyName("price")] Price`, reso nullable `SellerPrice`
5. `ICardTraderApiService` + `CardTraderApiClient` — aggiunto `GetOrderDetailAsync(int orderId)` per richiamare `GET /orders/{id}`; `GetOrdersAsync` ora arricchisce ogni ordine dal detail endpoint (necessario perché la list API non restituisce `seller_price` per item)
6. `InventorySyncService.SyncOrdersAsync()` — branch UPDATE aggiorna sia `Tag` sia `Price` sugli `OrderItem` esistenti
7. `CardTraderSyncOrchestrator` — aggiunto blocco `if (request.SyncOrders)` mancante + metodo privato `SyncOrdersAsync` che recupera gli ordini degli ultimi 7 giorni; ora la sync notturna (`ScheduledProductSyncWorker`) include effettivamente gli ordini
8. `CardTraderOrdersController` — nuovo endpoint `POST /api/cardtrader/orders/{cardTraderOrderId}/sync` per sync di un singolo ordine (utile per test mirati)
9. `ReportingController.GetTagProfitability` — riscritto per usare GROUP BY direttamente in SQL (fix timeout/500 da 31s); `TotaleAcquistato` letto da `PendingListings.PurchasePrice` (non da `InventoryItems`)
10. Due nuovi endpoint in `ReportingController`:
    - `GET /api/reporting/profitability/by-tag` — lista tag con totale acquistato, venduto, guadagno/perdita
    - `GET /api/reporting/profitability/by-tag/{tag}/expansions` — dettaglio per espansione del tag selezionato
11. Nuovi DTOs: `TagProfitabilityDto`, `TagExpansionProfitabilityDto`
12. `eCommerce.Inventory.Tests.csproj` — corretto target framework `net8.0` e versioni pacchetti; aggiunto mock `IScryfallApiClient` in `CardTraderSyncOrchestratorTests`

**Frontend:**
1. `reporting.models.ts` — aggiunte interfacce `TagProfitability` e `TagExpansionProfitability`
2. `reporting.service.ts` — aggiunti metodi `getTagProfitability()` e `getTagExpansionProfitability(tag)`
3. Nuovo componente `TagProfitabilityComponent` — griglia tag con drill-down per espansione al click
4. `reporting.module.ts` — dichiarato il nuovo componente
5. `reporting-routing.module.ts` — aggiunta route `/profitability/tags`
6. `layout.component.ts` — aggiunta voce "Redditività per Tag" nel menu laterale

---

### Fix e miglioramenti (2026-03-27)

**Problema 1 — Bottone sync singolo ordine non visibile**
Le modifiche erano rimaste nel worktree Claude e non erano state copiate nel repo principale.
- Copiati `orders-list.component.ts` e `cardtrader-api.service.ts` dal worktree al repo.
- La colonna "Azioni" con bottone sync icona appare correttamente nella griglia ordini.

**Problema 2 — Timeout 30s su `GET /api/reporting/profitability/by-tag`**
La query `pendingPrices` passava una lista `blueprintIds` come `OPENJSON` (potenzialmente migliaia di ID), causando timeout.
- Sostituita la doppia query (vendutoPerTagBlueprint + pendingPrices) con un unico JOIN SQL:
  `OrderItems GROUP BY (Tag, BlueprintId)` JOIN `PendingListings GROUP BY (BlueprintId, Tag)` → GROUP BY Tag.
- Nessuna lista di ID passata come parametro: tutto risolto via JOIN server-side.

**Problema 3 — TotaleAcquistato errato nel dettaglio espansioni**
La query calcolava il "costo del venduto" (avg prezzo × quantità venduta) invece del totale acquistato reale.
- Query corretta per espansione:
  ```sql
  SELECT e.Name, SUM(pl.Quantity * pl.PurchasePrice)
  FROM PendingListings pl
  INNER JOIN Blueprints b ON b.Id = pl.BlueprintId
  INNER JOIN Expansions e ON e.Id = b.ExpansionId
  WHERE pl.Tag = @tag
  GROUP BY e.Name
  ```

**Problema 4 — ValoreRimanente sempre zero**
La query moltiplicava `InventoryItems.PurchasePrice` (sempre 0 per gli item importati da CT) invece del prezzo di mercato.
- Corretto in `ReportingController` (livello Tag e livello Espansione): ora usa `InventoryItems.ListingPrice`.

**Problema 5 — Backfill Tag su OrderItems storici insufficiente**
Il primo backfill aggiornava solo gli item con `BlueprintId IS NOT NULL` — la maggior parte degli storici ce l'ha NULL.
- Riscritto `POST /api/cardtrader/orders/backfill-tags`:
  - Risolve il `BlueprintId` locale tramite `OrderItem.CardTraderId → Blueprints.CardTraderId → Blueprints.Id`
  - Aggiorna anche `OrderItem.BlueprintId` se era NULL
  - Copertura ancora parziale: OrderItems molto vecchi con CardTraderId non presente nei Blueprints locali non vengono matchati

**Nuova funzionalità — Grid State nel report Redditività per Tag**
Aggiunta gestione completa stato griglia su entrambe le griglie del componente `TagProfitabilityComponent`:
- Sidebar con pannello "Colonne" (mostra/nasconde colonne)
- Persistenza in `localStorage` via `GridStateService`
  - ID griglia Tag: `tag-profitability-tags-grid`
  - ID griglia Espansioni: `tag-profitability-expansions-grid`
- Ripristino automatico all'apertura della pagina
- Salvataggio su: spostamento colonna, visibilità, ridimensionamento, ordinamento

---

### Stato al 2026-03-27
- Build: 0 errori ✅
- Colonna "Azioni" con bottone sync visibile nella griglia ordini ✅
- Report `by-tag` risponde senza timeout ✅
- TotaleAcquistato allineato tra livello Tag e livello Espansione ✅
- ValoreRimanente calcolato correttamente con ListingPrice ✅
- Grid state (sidebar colonne + persistenza) attivo su entrambe le griglie ✅
- Backfill Tag OrderItems storici: copertura parziale (limitata dai blueprint non importati) ⚠️

### TODO
- Nessun TODO aperto su questa feature.
