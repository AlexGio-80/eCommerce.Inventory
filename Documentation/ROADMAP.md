# eCommerce.Inventory - Roadmap

> Aggiornare questo file a ogni sessione: spostare le voci tra le sezioni man mano che il lavoro avanza.

---

## In Corso

_Nessun task attivo al momento._

---

## Completato (Sessione 2026-09-05)

| Data | Voce |
|------|------|
| 2026-09-05 | Sicurezza — **Firma obbligatoria sul webhook Card Trader**: header `X-Signature` mancante o non valido ora restituisce `401` invece di processare l'evento. Sistemato anche l'ordine `EnableBuffering()`/model binding (nuovo resource filter `EnableRequestBodyBufferingAttribute`), altrimenti la rilettura del corpo per la firma sarebbe sempre stata vuota. Aggiunti test HTTP end-to-end con `TestServer` |

---

## Completato (Sessione 2026-09-02)

| Data | Voce |
|------|------|
| 2026-09-02 | Feature — **Esecuzione dell'autopricer a richiesta, che prosegue in background**: `POST /api/pricing/run` risponde `202` invece di restare aperto per ore, nuovo `PricingRunCoordinator` che ne tiene una sola alla volta (notturna inclusa), endpoint di stato e di interruzione, indicatore di avanzamento in barra di stato visibile da ogni pagina |
| 2026-09-02 | Feature — **Anteprima mirata**: filtri per fascia di prezzo ed espansione, per provare una regola sulle carte che quella regola riguarda invece che sempre sulle più care |
| 2026-09-02 | Feature — **Applicazione selettiva dall'anteprima**: caselle di selezione nella griglia e `POST /api/pricing/apply`, che scrive anche con il profilo in dry-run. È la strada per uscire dalla simulazione un pezzo alla volta |
| 2026-09-02 | Fix — **`forceApply` non bastava in un punto solo**: `PricingEngine` consulta `profile.DryRun` per conto proprio, quindi l'applicazione non scriveva nulla. Trovato da un test |

---

## Completato (Sessione 2026-08-29)

| Data | Voce |
|------|------|
| 2026-08-29 | Sicurezza — **API chiusa per difetto**: criterio globale con ruolo `Admin` richiesto su ogni endpoint, registrazione rimossa, password del seed non più fissa nel codice, endpoint di cambio password. In produzione: password di `admin` cambiata, utente `testuser` eliminato |
| 2026-08-29 | Pulizia — **Rimosso il controller webhook segnaposto** (`Controllers/CardTrader/`), mai implementato: si limitava a registrare il payload e a rispondere OK, duplicando la route del webhook vero |
| 2026-08-29 | Fix — **Confronto fra prezzo venditore e prezzi acquirente**: il motore ricava il fattore di conversione dalla propria inserzione nel feed e ragiona sulla posizione in vetrina |
| 2026-08-29 | Feature — **Collocazione percentuale** al posto della posizione fissa, e riferimento che non può mai cadere sull'offerta più cara |
| 2026-08-29 | Feature — **Guardrail asimmetrico** (+300% in salita, −25% in discesa) e **filtro di rapporto sulla mediana** sempre attivo contro prezzi di comodo e prezzi da neofita |
| 2026-08-29 | Feature — **La vendita scala subito la giacenza locale**, con guardia sui webhook duplicati; niente più rivalutazioni sprecate su carte esaurite |
| 2026-08-29 | Feature — **Storico dei prezzi** alimentato dalla sync notturna, a delta, con quantità accanto al prezzo. Base per i grafici di andamento |
| 2026-08-29 | Fix — **Log di produzione**: la cartella corrente di un servizio Windows è System32, il sink finiva lì. Corretti anche `publish.ps1` (SID invece del nome localizzato) e gli enricher inesistenti |

---

## Completato (Sessione 2026-08-28)

| Data | Voce |
|------|------|
| 2026-08-28 | Fix — **Sincronizzazione inventario ripristinata**: ferma dal 03/12/2025 per un'eccezione su chiave duplicata nel lookup da `PendingListings`. Deriva recuperata: 282 articoli venduti da rimuovere, 192 carte da inserire, 203 quantità da riallineare |
| 2026-08-28 | Fix — **I fallimenti parziali di sync non sono più riportati come successo**: l'esito complessivo e la metrica `ecommerce_sync_total` riflettono ora le sezioni fallite |
| 2026-08-28 | Fix — **Log di produzione resi visibili**: `MinimumLevel` da `Warning` a `Information` con `Override` sui namespace di framework; rimosso il doppio sink File causato dal merge per indice degli array di configurazione |
| 2026-08-28 | Fix — **Storico prezzi delle carte vendute preservato**: foreign key da `CASCADE` a `SET NULL` (migration `PreservaStoricoPrezziCarteVendute`) |
| 2026-08-28 | Feature — **Dettaglio carta per carta delle esecuzioni dell'autopricer** nella scheda Storico, con filtro per esito |

---

## Completato (Sessione 2026-08-20)

| Data | Voce |
|------|------|
| 2026-08-20 | Feature — Ricerca blueprint per **Collector Number** e **Nome Italiano** nel selector "Nuovo Prodotto": campo `ItalianName` su Blueprint (lazy-popolato via Scryfall `localized.it` durante sync), `SearchByNameAsync` esteso per matchare `collector_number` (JSON) e `ItalianName`, autocomplete mostra nome italiano quando disponibile |

---

## Da Fare

### Sicurezza — i quattro punti del 2026-08-27 sono chiusi

Tutti erano **preesistenti**, non introdotti dal lavoro sull'autopricer. Codice risolto e messo in produzione il 2026-08-29, password di `admin` cambiata e utente `testuser` eliminato lo stesso giorno. Dettaglio nel CHANGELOG.

Ne restava uno sul webhook, risolto; uno era da verificare ed è emerso che il webhook non è
mai stato raggiungibile dall'esterno, per due motivi distinti — indagine e fix del 2026-09-05:

- [x] ~~**La firma del webhook si aggira omettendo l'header**~~ — risolto il 2026-09-05: header `Signature` mancante o non valido restituisce `401`. Sistemato anche l'ordine `EnableBuffering()`/model binding con un resource filter dedicato, altrimenti la rilettura del corpo per la firma sarebbe sempre stata vuota (dubbio lasciato aperto dalla nota precedente, ora verificato con test HTTP end-to-end)
- [x] ~~**Verificare com'è instradato il webhook dall'esterno**~~ — verificato il 2026-09-05: non lo era. Il pannello Card Trader non ha mai avuto un `webhook_url` configurato, e il controller cercava comunque l'header sbagliato (`X-Signature` invece di `Signature`, vedi sopra) con lo shared secret di produzione ancora al placeholder. Tre problemi indipendenti che si sommavano
- [ ] **In stand-by (deciso il 2026-09-05): registrare il `webhook_url` ed esporre l'endpoint dall'esterno**. La macchina è dietro un router di casa: aprire una porta per un webhook opzionale non vale il rischio, dato che il giro con ricezione ordini a polling tiene comunque l'inventario allineato. Se in futuro si riprende: (1) registrare l'URL nel pannello Card Trader (campo "Indirizzo del tuo endpoint webhook", oggi vuoto); (2) Kestrel ascolta solo su `127.0.0.1:5152`, IIS su `*:80` non ha nessuna regola che inoltri `/api/*` verso di lui — serve reverse proxy IIS→Kestrel (Application Request Routing + URL Rewrite) oppure esporre Kestrel direttamente, più il port forwarding sul router

### Autopricer — taratura dopo le prime notti in simulazione

- [x] ~~**Rivedere la posizione nella fascia 25–100 €**~~ — risolto il 2026-08-29 sostituendo l'ordinale con la collocazione percentuale
- [x] ~~**Caso limite offerte == posizione**~~ — risolto il 2026-08-29: il riferimento non può più coincidere con l'offerta più cara, in nessuna modalità
- [ ] **Decidere quando uscire dal dry-run**: la scheda Storico ha ora il dettaglio carta per carta con filtro per esito, quindi il confronto fra proposte e proprie attese si può fare direttamente dall'interfaccia. Dal 2026-09-02 non è più una decisione tutto-o-niente: «Applica» dall'anteprima scrive sulle sole carte spuntate anche a profilo in dry-run, quindi si può cominciare da un insieme piccolo e guardato uno per uno
- [ ] **Affinare i percentili guardando l'anteprima**: partenza a 15% sul bulk, 20% fra 1 e 25 €, 40% sopra i 25 €. Dal 2026-09-02 l'anteprima si può restringere per fascia di prezzo ed espansione, quindi ogni percentile si prova sulle carte che governa. L'analisi di sensibilità su 11 carte reali mostra che le carte davvero sottoprezzo danno lo stesso risultato dal 20% al 60% — il segnale è robusto — mentre il percentile decide sulle altre. Da notare che sui mercati profondi il percentile è più aggressivo del vecchio ordinale (su Overgrown Tomb si passa dalla terza alla quinta posizione)
- [ ] **Grafici di andamento prezzi**: la raccolta parte dalla notte del 29/08. Ha senso disegnarli quando ci saranno qualche settimana di dati; la serie utile è prezzo e quantità da `PriceHistoryEntries`, affiancata al riferimento di mercato da `PriceChangeLogs`
- [ ] **Allineare la scala di `PriceChangeLogs.ReferencePrice`**: è un prezzo di vetrina mentre `OldPrice` e `ProposedPrice` sono prezzi venditore, quindi su un grafico finirebbero sullo stesso asse pur misurando cose diverse. Il motore calcola già il prezzo di vetrina, lo scrive solo nella motivazione: basta una colonna
- [ ] **Valutare se i ribassi vadano concessi affatto**: con il percentile al 40% restano 5 carte su 11 con proposta in ribasso. Il guardrail le limita al 25%, ma si può anche disattivare `CanDecrease` per fascia se l'obiettivo è solo cogliere i rialzi
- [ ] **Forzare il prezzo proposto sulle carte bloccate dal guardrail, dalla scheda Storico** (emerso il 2026-09-04): oggi una carta con esito `BlockedByGuardrail` resta al prezzo vecchio anche rilanciando l'esecuzione, perché il blocco è nel motore (`PricingEngine.Evaluate`), non nella modalità di scrittura — nemmeno `ForceApply` lo scavalca. Serve lo stesso pattern già costruito per l'anteprima: caselle di selezione nella griglia dei calcoli (Storico → dettaglio esecuzione) e un pulsante "Applica comunque" che rivaluta le carte scelte su dati freschi ma ignora il guardrail solo per loro. Tecnicamente serve un nuovo parametro (tipo `bypassGuardrail`) da far arrivare a `Evaluate` con lo stesso meccanismo di `ForceApply`/`ForceDryRun`

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
