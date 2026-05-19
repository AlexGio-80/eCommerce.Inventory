# eCommerce.Inventory - Roadmap

> Aggiornare questo file a ogni sessione: spostare le voci tra le sezioni man mano che il lavoro avanza.

---

## In Corso

_Nessun task attivo al momento._

---

## Da Fare

_Nessun task attivo al momento._

---

## Backlog / Idee Future

> Funzionalità non prioritarie, da rivalutare in futuro.

- [ ] Redis caching per dati statici Card Trader (Games TTL 24h, Expansions TTL 12h, Blueprints TTL 6h)
- [ ] Espansione multi-marketplace (eBay, Wallapop) — pattern già definito in ARCHITECTURE.md
- [ ] AI Grading reale (Ximilar API) — valutare costi/benefici abbonamento
- [ ] Health check endpoint `/health` con controllo DB + Card Trader API
- [ ] Monitoring (Application Insights o equivalente)
- [ ] CI/CD pipeline (GitHub Actions)

---

## Completato

| Data | Voce |
|------|------|
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
