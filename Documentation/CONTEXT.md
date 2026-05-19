# eCommerce.Inventory - Contesto di Sessione

> Punto di partenza per ogni nuova sessione con Claude.
> Aggiornare dopo ogni sessione significativa.
> **Non è un documento di architettura** (quello è ARCHITECTURE.md) — è uno snapshot dello stato corrente per riprendere velocemente senza rileggere tutto.

---

## Stato Attuale

**Branch principale:** `master`
**Ultimo aggiornamento:** 2026-05-19 (sessione 2)
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
- Report Redditività per Tag con drill-down per Espansione
- Report Vendite, Inventario, Redditività ROI
- Expansion Analytics (valore medio carte per espansione via Card Trader API)
- AI Grading mock (Ximilar API non attivata)
- Rate limiter outbound Card Trader (20 req/min)
- Backup giornaliero automatico (DB + applicazione)
- Icone espansioni e date rilascio da Scryfall

### Cosa è in sospeso / da verificare
- Possibile disallineamento residuo tra il valore `TotaleAcquistato` a livello Tag e la somma dei valori per Espansione nel report Redditività per Tag
- Copertura limitata del backfill Tag su OrderItems storici (molti `CardTraderId` non trovano corrispondenza nei Blueprints locali)

---

## Decisioni Recenti

| Data | Decisione | Perché |
|------|-----------|--------|
| 2026-05-19 | Sync UPDATE aggiorna anche `InventoryItem` locale oltre a CT | Il pannello "Le mie inserzioni" legge da `InventoryItems`; senza aggiornamento locale i valori restano obsoleti fino alla sync notturna |
| 2026-05-19 | `PendingListing.IsUpdate` come flag per distinguere CREATE vs UPDATE su CT | Evita duplicazione di inserzioni quando si modifica una carta già listata su CT |
| 2026-05-19 | Pannello inserzioni usa `InventoryItems` come fonte (non solo PendingListings) | Mostra tutto ciò che è su CT (incluse inserzioni "ct-native" mai gestite dal software) |
| 2026-03-27 | `TotaleAcquistato` nel report Tag usa JOIN diretto con `PendingListings` | Eliminare la query con `OPENJSON` che causava timeout 30s |
| 2026-03-27 | `ValoreRimanente` usa `InventoryItems.ListingPrice` (non `PurchasePrice`) | `PurchasePrice` sugli InventoryItems è spesso zero; il prezzo di mercato attuale è `ListingPrice` |
| 2026-02-22 | Integrazione Scryfall per icone SVG e date rilascio espansioni | Migliorare UX griglia "Items to Prepare" con riferimenti visivi |
| 2025-12-22 | `SyncSettings:RunAnalyticsDuringSync = false` di default | La sync analytics durante la nightly causava stalli del sistema |

---

## Punti di Attenzione

- La migration più recente è `20260519073801_AddIsUpdateToPendingListings` — da applicare in produzione (`dotnet ef database update`)
- La migration `20260326145049_AddTagToOrderItems` è già applicata in produzione
- Il backfill Tag (`POST /api/cardtrader/orders/backfill-tags`) ha copertura parziale: funziona solo per OrderItems con `CardTraderId` che trova corrispondenza nei Blueprints locali
- Il file `debug_expansion_{id}.csv` viene generato da `ExpansionAnalyticsService` durante l'analisi — non committare nella repo
- L'AI Grading usa un mock service: il servizio reale Ximilar richiede abbonamento a pagamento
- Il seed crea sempre un utente `admin` — la logica controlla che non duplichi

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
| `src/eCommerce.Inventory.Api/Program.cs` | Bootstrap DI, middleware, configurazione |
| `src/eCommerce.Inventory.Infrastructure/Persistence/ApplicationDbContext.cs` | DbContext + DbSet |
| `src/eCommerce.Inventory.Infrastructure/DependencyInjection.cs` | Registrazione servizi Infrastructure |
| `src/eCommerce.Inventory.Infrastructure/CardTrader/CardTraderSyncOrchestrator.cs` | Orchestrazione sync completa Card Trader |
| `src/eCommerce.Inventory.Api/Controllers/ReportingController.cs` | Endpoint reporting (query SQL pesanti) |
| `src/eCommerce.Inventory.Api/appsettings.json` | Configurazione (senza segreti) |
| `publish.ps1` | Script deploy automatizzato (richiede permessi Admin) |
| `frontend/ecommerce-inventory-ui/src/app/` | Root Angular app |

---

## Ambiente di Sviluppo

| Voce | Valore |
|------|--------|
| Database | `ECommerceInventory` su `DEV-ALEX\MSSQLSERVER01` |
| Backend (dev) | `http://localhost:5152` |
| Frontend (prod) | `http://inventory.local` (IIS) |
| Frontend (dev) | `http://localhost:4200` |
| Log backend | `Publish/api/logs/` |
