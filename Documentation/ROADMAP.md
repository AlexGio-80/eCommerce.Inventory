# eCommerce.Inventory - Roadmap

> Aggiornare questo file a ogni sessione: spostare le voci tra le sezioni man mano che il lavoro avanza.

---

## In Corso

_Nessun task attivo al momento._

---

## Completato (Sessione 2026-08-20)

| Data | Voce |
|------|------|
| 2026-08-20 | Feature — Ricerca blueprint per **Collector Number** e **Nome Italiano** nel selector "Nuovo Prodotto": campo `ItalianName` su Blueprint (lazy-popolato via Scryfall `localized.it` durante sync), `SearchByNameAsync` esteso per matchare `collector_number` (JSON) e `ItalianName`, autocomplete mostra nome italiano quando disponibile |

---

## Da Fare

### Sicurezza (emerso il 2026-08-27, da affrontare insieme)

Tutti punti **preesistenti**, non introdotti dal lavoro sull'autopricer.

- [ ] **`POST /api/auth/register` è aperto a chiunque**: `AuthController` non ha l'attributo `[Authorize]`, quindi chiunque raggiunga l'API può crearsi un account senza credenziali. Da decidere: chiuderlo dietro autenticazione oppure disabilitarlo del tutto, dato che l'utente è uno solo
- [ ] **Il ruolo non è verificato da nessuna parte**: non esiste un solo `[Authorize(Roles = "Admin")]` nel progetto. Gli account creati da `register` hanno ruolo `User` ma accedono a tutto — inventario, ordini, autopricer compreso. Combinato col punto precedente significa che chi raggiunge l'API ha pieno accesso al magazzino
- [ ] **Cambiare la password di `admin`**: oggi è `admin123`, cioè il valore scritto nella documentazione di un repository GitHub **pubblico**
- [ ] **Rimuovere l'utente `testuser`**, residuo dei test iniziali

> Nota sull'esposizione: l'API ascolta su `localhost:5152`, ma l'endpoint webhook dev'essere raggiungibile da Card Trader, quindi un varco verso l'esterno probabilmente esiste. Da verificare com'è instradato.

### Autopricer — taratura dopo le prime notti in simulazione

- [ ] **Rivedere la posizione nella fascia 25–100 €**: la regola chiede la posizione 4, ma su quelle carte il mercato ha spesso 2–3 venditori comparabili, quindi molte valutazioni vengono saltate. Valutare se scendere a 2 o 3
- [ ] **Caso limite offerte == posizione**: con esattamente 4 offerte e posizione 4 il motore prende comunque la più cara (fermato dal guardrail). Valutare se estendere il salto anche a questo caso
- [ ] **Decidere quando uscire dal dry-run**: guardare la scheda Storico dopo qualche notte e confrontare le proposte con le proprie attese prima di attivare la scrittura reale

---

## Backlog / Idee Future

> Funzionalità non prioritarie, da rivalutare in futuro.

- [x] **Redis caching per dati statici Card Trader** (Games TTL 24h, Expansions TTL 12h, Blueprints TTL 6h) — **COMPLETATO 2026-08-24**
- [x] **Health check endpoint `/health`** con controlli DB, Card Trader API, Redis — **COMPLETATO 2026-08-24**
- [x] **Monitoring/Observability Fase 1** (Prometheus `/metrics`, OpenTelemetry tracing, Correlation ID, Serilog da appsettings) — **COMPLETATO 2026-08-27**
- [ ] AI Grading reale (Ximilar API) — valutare costi/benefici abbonamento
- [ ] **Monitoring Fase 2** — backend di raccolta per trace e metriche (oggi OpenTelemetry usa il Console exporter, quindi niente storico). Da valutare: Prometheus + Grafana in locale, oppure Application Insights
- [ ] **Installare Redis** per riattivare il caching dei dati statici Card Trader (codice già pronto, oggi `Enabled: false` perché il server non è installato)
- [ ] CI/CD pipeline (GitHub Actions)

---

## Completato

| Data | Voce |
|------|------|
| 2026-08-27 | Feature — **Autopricer custom**: motore a regole con scarto outlier, guardrail e dry-run; esecuzione notturna a copertura rotante; reprice immediato dopo vendita via webhook; interfaccia con Regole, Anteprima, Copertura e Storico |
| 2026-08-27 | Fix — **Ripristino wiring di produzione**: riattivati `UseWindowsService`, `UseUrls`, `ScheduledProductSyncWorker`, `BackupService` e i servizi one-shot, tutti rimasti commentati dopo il debug del monitoring; servizio Windows ricreato e deploy verificato |
| 2026-08-27 | Fix — **`/health` da 15,4s/503 a 0,19s/200**: `Redis:Enabled` allineato alla realtà (server non installato), check Card Trader degradato invece che unhealthy |
| 2026-08-27 | Feature — **Monitoring/Observability Fase 1**: endpoint `/metrics` Prometheus con 20 metriche business, distributed tracing OpenTelemetry, middleware Correlation ID, Serilog configurato da appsettings per environment, Health Checks UI |
| 2026-08-24 | Fix — **Disallineamento `TotaleAcquistato` report Redditività per Tag**: uniformata query `GetTagExpansionProfitability` a usare join espliciti (`from pl join bp join ex`) come `rimanentePerExpansion`, risolvendo differenza tra livello Tag (include record con Blueprint/Expansion NULL) e livello Espansione (escludeva quei record per INNER JOIN implicito) |
| 2026-08-24 | Feature — **Health check endpoint `/health`** con controlli Database (SQL Server), Card Trader API (via cached Games endpoint), Redis (se abilitato con graceful degradation); per liveness/readiness probes in produzione |
| 2026-05-20 | Fix — Items to Prepare: rimosso `domLayout: 'autoHeight'`, griglia ora rispetta altezza container, paginazione visibile a qualsiasi zoom |
| 2026-05-20 | Fix — Griglia Espansioni: layout flex sostituisce altezza fissa 600px, riempie tutto lo spazio disponibile |
| 2026-05-20 | Fix — Sidenav container: `calc(100vh - 112px)` per tenere conto di toolbar + tab-bar |
| 2026-05-19 | UX Review — rimozione componente `profitability-analysis` (dati inaffidabili) |
| 2026-05-19 | Feature — `tag-profitability` come tab dedicato nella Dashboard (invece di voce di menù separata) |
| 2026-05-19 | Miglioramento — Report Inventario: AG Grid con filtri/sort, soglia slow-movers configurabile, 4 KPI, fix EF LINQ translation |
| 2026-05-19 | Miglioramento — Report Vendite: filtro date, griglia top prodotti AG Grid con stato persistente, rimozione grafici inutilizzati |
| 2026-05-19 | Fix — Widget "Ultimo Sync" dashboard: ora legge `lastSyncTime` da `localStorage` invece di mostrare sempre l'ora corrente |
| 2026-05-19 | UX — Rimozione widget "Espansioni più Convenienti" dalla dashboard (valori non aggiornati, poco utile) |
| 2026-05-19 | Feature — Calcolatore Box su pagina Espansioni: PacksPerBox + CardsPerPack + BoxPrice salvati a DB; ROI% e breakeven calcolati on-the-fly |
| 2026-05-19 | Feature — Colonna "ROI Box%" in griglia Espansioni: colorata (verde/arancio/rosso), filtrabile e ordinabile |
| 2026-05-19 | Feature — UI button re-sync singolo ordine nella griglia Ordini (già presente nel codice, ROADMAP aggiornata) |
| 2026-05-19 | Feature — Pannello "Le mie inserzioni" in Nuovo Prodotto, update CT API implementato, flag IsUpdate su PendingListing |
| 2026-05-19 | Fix Qtà/Valore Rimanente nel report Redditività per Tag — query riscritte via PendingListings, endpoint backfill-tags per InventoryItems |
| 2026-03-27 | Feature 003 (cont.) — fix report Tag (query timeout), backfill Tag storici, grid state Redditività per Tag |
| 2026-03-26 | Feature 003 — Import Tag e Price su OrderItems, report Redditività per Tag con drill-down per Espansione |
| 2026-02-22 | Items to Prepare — icone espansione (Scryfall), date rilascio, nuovo pulsante Prepare |
| 2026-02-06 | Create Listing — prezzi suggeriti filtrati per condizione/lingua/foil/signed + tetto 1000€ |
| 2026-02-06 | Blueprint Sync — fix aggiornamento record esistenti (tutti i 14 campi) |
| 2025-12-23 | Expansion Analytics — fix calcolo valori (fetch per `expansion_id`, filtro `tournament_legal`) |
| 2025-12-22 | Expansion Analytics — ottimizzazione performance (batch 50 blueprints, config `RunAnalyticsDuringSync`) |
| 2025-12-21 | Expansion Analytics — valore medio carte per espansione, widget dashboard, progress SignalR |
| 2025-12-04 | AI Card Grading (mock) — grading con webcam, integrazione in Create Listing, persistenza su PendingListing |
| 2025-12-01 | UI Localization — tutto in italiano (UI, report, menu) + fix `ApiResponse<T>` wrapper nei servizi Angular |
| 2025-11-30 | Dashboard Final Polish — ordinamento ROI%, fonte dati ExpansionsROI view |
| 2025-11-30 | Inventory Actions — semplificazione (rimosso Edit/Delete, link diretto Card Trader) |
| 2025-11-29 | Dashboard Improvements — ROI widget, filtri testo, fix navigazione tab, fix query all-time |
| 2025-11-28 | Rate Limiting outbound Card Trader (20 req/min) + Backup giornaliero automatico |
| 2025-11-27 | Authentication JWT (login/register, BCrypt, AuthGuard, AuthInterceptor) |
| 2025-11-26 | Deployment — Windows Service + IIS, `publish.ps1`, `setup-iis.ps1` |
| 2025-11-25 | Reporting & Analytics — 10 endpoint, 3 dashboard (Vendite, Inventario, Redditività) |
| 2025-11-25 | Multi-Tab Navigation (TabManagerService, drag-and-drop, grid state per tab) |
| 2025-11-25 | Unprepared Items Grid — sync toolbar, date pickers, auto-sync 5 min |
| 2025-11-25 | API Controller Standardization — `ApiResponse<T>` su tutti i controller |
| 2025-11-24 | Orders Grid — multi-column sort, grid state persistence, badge condizioni, flag lingue |
| 2025-11-23 | Orders Management — backend + frontend, manual sync con date filter, SignalR |
| 2025-11-22 | AG-Grid integration — column visibility, grid state persistence, paginazione server-side |
| 2025-11-21 | Create Listing — pending listings, price suggestions, sync to Card Trader |
| 2025-11-20 | Games Management Page |
| 2025-11-19 | Angular Frontend setup (Phase 3.0 + 3.1 — Dashboard + Inventory List) |
| 2025-11-19 | Backend Testing — 14 test (webhook signature, handler, integration) |
| 2025-11-18 | Webhook Processing — HMAC SHA256, MediatR handler, order.create/update/destroy |
| 2025-11-18 | Card Trader Sync — DTOMapper, InventorySyncService, SyncWorker completo |
| 2025-11-18 | Database & Migrations — schema 6 tabelle, seed data, indici |
| 2025-11-17 | Setup iniziale — Clean Architecture 4 layer, entità Domain, DbContext, Repository, Serilog |
