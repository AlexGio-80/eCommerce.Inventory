# eCommerce.Inventory - Contesto di Sessione

> Punto di partenza per ogni nuova sessione con Claude.
> Aggiornare dopo ogni sessione significativa.
> **Non è un documento di architettura** (quello è ARCHITECTURE.md) — è uno snapshot dello stato corrente per riprendere velocemente senza rileggere tutto.

---

## Stato Attuale

**Branch principale:** `master`
**Ultimo aggiornamento:** 2026-05-20 (sessione 3 — fix layout griglia)
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
- Expansion Analytics (valore medio carte per espansione via Card Trader API)
- AI Grading mock (Ximilar API non attivata)
- Rate limiter outbound Card Trader (20 req/min)
- Backup giornaliero automatico (DB + applicazione)
- Icone espansioni e date rilascio da Scryfall

### Cosa è in sospeso / da verificare
- Possibile disallineamento residuo tra il valore `TotaleAcquistato` a livello Tag e la somma dei valori per Espansione nel report Redditività per Tag
- Copertura limitata del backfill Tag su OrderItems storici (molti `CardTraderId` non trovano corrispondenza nei Blueprints locali)
- Applicare le migration manuali su server di produzione (SQL diretto: vedi sezione Punti di Attenzione)

---

## Decisioni Recenti

| Data | Decisione | Perché |
|------|-----------|--------|
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

---

## Punti di Attenzione

- **Migrazioni manuali**: `20260519120000_AddBoxConfigToExpansions` e `20260519130000_AddBoxPriceToExpansions` applicate via SQL diretto (non `dotnet ef database update`). Registrate in `__EFMigrationsHistory`, snapshot aggiornato. Non hanno `.Designer.cs`. Il prossimo `migrations add` funzionerà correttamente.
- In produzione applicare le migration tramite SQL diretto (stessa procedura): `ALTER TABLE Expansions ADD PacksPerBox int NULL, CardsPerPack int NULL, BoxPrice decimal(18,2) NULL`
- La migration EF ufficiale più recente è `20260519073801_AddIsUpdateToPendingListings`
- Il backfill Tag (`POST /api/cardtrader/orders/backfill-tags`) ha copertura parziale
- Il file `debug_expansion_{id}.csv` viene generato da `ExpansionAnalyticsService` — non committare
- L'AI Grading usa un mock service: Ximilar richiede abbonamento a pagamento
- Il seed crea sempre un utente `admin` — la logica controlla che non duplichi

---

## Backlog Tecnico (Punto Aperto)

- **Sealed Product Sync** — prezzi box automatici: recuperare prezzo box sigillati da CT (primi 10 valori più bassi in inglese tra Blueprint categoria "sealed") per pre-popolare `BoxPrice`. Al momento inserimento manuale.

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
| `eCommerce.Inventory.Api/Controllers/ReportingController.cs` | Endpoint reporting (query SQL pesanti) |
| `eCommerce.Inventory.Api/Controllers/ExpansionsController.cs` | Gestione espansioni + calcolatore box (BoxConfigDto, BoxRoiPercentage) |
| `eCommerce.Inventory.Api/appsettings.json` | Configurazione (senza segreti) |
| `publish.ps1` | Script deploy automatizzato (richiede permessi Admin) |
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
