# eCommerce.Inventory - Richieste di Implementazione

> Documento di progetto della feature. L'analisi manuale fatta dall'utente per l'uscita di Star Trek,
> da cui la feature nasce, è in `Features/004-ProductBuying.md` (radice del repository) e resta lì
> come documentazione di partenza.

---

## 1. Descrizione

**Titolo feature:** `Analisi acquisto prodotti sigillati`
**Priorità:** Alta

Quando esce un'espansione, e anche sulle espansioni già uscite, l'utente deve decidere **cosa
comprare** (quale formato: Play Box, Collector Box, Draft Night, bundle, Commander, Scene Box...),
**se conviene** (valore atteso dell'apertura contro prezzo d'acquisto), **quando comprare**
(andamento del prezzo dal preordine in poi), **quanti** e se **aprire o tenere sigillato**. Oggi lo fa
a mano, con conteggi presi da fonti inaffidabili e un confronto per "costo a carta" che mette sullo
stesso piano una carta di una Play Booster e una di una Collector. La feature automatizza i dati,
il calcolo e gli avvisi.

### Decisioni prese con l'utente (2026-10-07)

| Tema | Decisione |
|------|-----------|
| Dove si compra | Su **Cardmarket** (CM), di solito più conveniente. Si vende su Card Trader (CT) |
| Fonte prezzi di acquisto | Listino prezzi giornaliero pubblico di CM. **Riferimento `trend`, con `low` mostrato accanto** |
| Fonte prezzi di vendita | Mercato CT, con lo stesso criterio delle offerte comparabili usato dall'autopricer |
| Lingua | Si compra **solo in inglese**. Il listino CM non distingue la lingua per i sigillati: va bene così |
| Prodotti analizzati | **Tutti i tipi**: buste e box, Collector, Draft Night, bundle, Commander, Scene Box, Prerelease... Spesso hanno carte esclusive con ottima resa |
| Bulk | Si carica **tutto** su CT, token compresi: comuni, non comuni e token si valutano al prezzo del bulk pesato per quanto se ne vende davvero, non a zero |
| Avvisi | **Notifica nell'app + email**. Entrambi solo in uscita: nessuna porta da aprire (vedi decisione del 2026-09-05 sul webhook) |
| Tracciamento aperture | Si torna al Tag per apertura, formato `CODICE_TIPO_AAAAMMGG` (es. `TRK_PB_20261115`, `TRK_CB_20261115`). Il report per Tag andrà poi raggruppato per prefisso per riavere la vista per espansione |
| API Cardmarket | **Non disponibili**: CM non accetta nuove richieste e l'utente non ha credenziali precedenti. La feature usa solo i file pubblici, in sola lettura |
| Doppia vendita CT + CM | **Fuori da questa feature.** Senza API CM non è fattibile; resta in backlog |
| Scraping di pagine CM | **Escluso**: CM ha protezioni anti-bot e il listino pubblico copre già tutti i prodotti |

---

## 2. Componenti Coinvolti

- [x] Backend (API / Infrastructure / Domain)
- [x] Frontend (Angular)
- [x] Database (nuove migration, una per fase)
- [x] Integrazione esterna — Card Trader API (già esistente), **file pubblici Cardmarket**, **MTGJSON**, invio email (SMTP)

---

## 3. Fonti Dati (verificate il 2026-10-07)

### Cardmarket — listino e catalogo pubblici

Pubblici, senza login, aggiornati ogni giorno (annuncio CM: «price guide and product catalogue available for download»):

| File | URL | Dimensione | Note |
|------|-----|-----------|------|
| Listino prezzi Magic | `https://downloads.s3.cardmarket.com/productCatalog/priceGuide/price_guide_1.json` | ~26 MB | Generato intorno alle 01:00 italiane. Per prodotto: `avg`, `low`, `trend`, `avg1`, `avg7`, `avg30` e i corrispettivi `-foil` |
| Catalogo sigillati | `.../productCatalog/productList/products_nonsingles_1.json` | ~1 MB | `idProduct`, `name`, `categoryName`, `idExpansion`, `dateAdded` |
| Catalogo singole | `.../productCatalog/productList/products_singles_1.json` | ~20 MB | Serve solo se manca l'abbinamento da Card Trader |

Il suffisso `_1` è il gioco (1 = Magic). Il listino contiene tutti i prodotti Star Trek del documento
dell'utente, già in preordine (es. trend del 07/10: Play Box 140,35 €, Collector Box 428,32 €, Draft
Night 98,21 €, Deck Set 219,95 €, Scene Box Set 70,36 €). Su un preordine `avg1`/`avg7`/`avg30` sono
`null` finché non ci sono vendite.

### Abbinamento Card Trader ↔ Cardmarket — già presente

`Blueprint.CardMarketIds` (JSON array, dal campo `card_market_ids` dei blueprint CT) è valorizzato su
121.539 blueprint su 123.983, **compresi i sigillati** (es. "Star Trek Play Booster Box" → `[897524]`).
Non serve nessun abbinamento a mano.

### MTGJSON — contenuto dei prodotti e composizione delle buste

`https://mtgjson.com/api/v5/{SET}.json` (già usato da `PopulateItalianNamesService` per i nomi italiani):

- `sealedProduct[]`: ogni prodotto sigillato con `category`/`subtype`, `identifiers.mcmId` (id CM) e
  `contents` (buste, altri sigillati, mazzi, carte, extra). Es. Play Booster Box = 30 × Play Booster
  Pack; Draft Night = 12 Play Booster + 1 Collector Booster + terre e token.
- `booster{}`: per tipo di busta (`play`, `collector`, ...) le configurazioni possibili con il loro
  peso e i fogli di stampa (`sheets`) con le carte e i loro pesi, cioè **le probabilità vere** di
  rara, mitica, foil, variante. Per Star Trek (`TRK`, uscita 13/11/2026) i prodotti ci sono già, la
  composizione delle buste **non ancora**: di solito arriva intorno all'uscita.

---

## 4. Fasi

Ogni fase si pubblica e si verifica in produzione prima della successiva.

### Fase 0 — Import giornaliero del listino Cardmarket *(da fare per prima: ogni giorno perso è storico perso)*

Un `BackgroundService` scarica ogni mattina catalogo sigillati e listino, e li salva in SQL con lo
storico. Non usa l'API Card Trader, quindi non tocca il limite di 20 richieste al minuto e non va
coordinato con la notturna. Se `Last-Modified` non è cambiato rispetto all'ultimo import, salta.

**Cosa si salva:**
- **Sigillati: tutti, ogni giorno** (qualche migliaio di righe al giorno). La serie storica resta
  semplice da interrogare, ed è il dato su cui si basa "quando comprare".
- **Singole: solo a variazione**, come `PriceHistoryEntries`, e solo per le espansioni uscite negli
  ultimi N mesi più quelle in preordine (N configurabile). Il listino completo ha oltre centomila
  prodotti: salvarlo tutto ogni giorno sarebbe decine di milioni di righe all'anno senza beneficio.

**Implementazione (2026-10-07):**
- `CardmarketPriceImportService` (logica), `CardmarketImportWorker` (all'avvio del servizio + ogni
  giorno a `CardmarketImport:RunTime`, default 07:00), `CardmarketDownloadClient` (file pubblici),
  `CardmarketController` (`POST /api/cardmarket/import`, `GET /api/cardmarket/import/logs`,
  `GET /api/cardmarket/products/{idProduct}/prices`). Pulsante "Listino Cardmarket" nella pagina
  Espansioni: tooltip con l'esito dell'ultimo import, icona rossa se è fallito, clic per lanciarlo.
- Tabelle `CardmarketProducts`, `CardmarketPriceSnapshots` (chiave prodotto + giorno),
  `CardmarketImportLogs`. Migration `20261007100424_AddCardmarketPriceHistory`.
- **Singole seguite**: quelle delle espansioni la cui **prima carta** è comparsa nel catalogo CM
  negli ultimi `CardmarketImport:SinglesTrackingMonths` mesi (default 12). Non si usa la data di
  uscita di `Expansion`, perché le espansioni "Collectors" di Card Trader (es. "Star Trek
  Collectors") non ce l'hanno: si sarebbero perse proprio le carte Collector. Al 07/10: 61
  espansioni, 7.514 singole.
- **Giorno della serie** = data del `createdAt` del listino nel suo fuso, non quella dell'import.
- **Variazione di una singola** = cambia `trend`, `low`, `avg` o uno dei foil. `avg1`/`avg7`/`avg30`
  esclusi dal confronto: cambiano quasi ogni giorno e annullerebbero la serie a variazione.
- Se `Last-Modified` del listino è uguale all'ultimo import riuscito non si scarica nulla; un giorno
  già presente non viene mai riscritto, nemmeno con `force`.
- **Provato su SQL Server** con un database a parte, cancellato dopo la prova: primo import in 9 s
  (5.099 sigillati + 7.514 singole), secondo avvio saltato su `Last-Modified`, giorno successivo
  simulato con 5.099 sigillati e solo le 10 singole modificate.

**Criteri di accettazione:**
- [x] L'import gira da solo ogni giorno e registra esito, durata e righe scritte
- [x] Per i prodotti Star Trek si accumula una riga al giorno con `trend`, `low` e `avg` — **pubblicato e verificato il 2026-10-07**: primo import riuscito in 9 s (5.099 sigillati, 7.514 singole, Play Box TRK a 140,35 € di trend). Resta da vedere nei prossimi giorni che la serie cresca di una riga al giorno
- [x] Un fallimento del download non blocca il resto dell'applicazione ed è visibile (registro + icona rossa nella pagina Espansioni)
- [x] Le singole si scrivono solo se un valore cambia

### Fase 1 — Contenuto dei prodotti e convenienza fra formati

Import da MTGJSON dei prodotti sigillati e del loro contenuto, collegati all'id CM e al blueprint CT.
Per ogni espansione, una tabella che riporta ogni prodotto a **busta equivalente** (Play / Collector /
altro) e mostra il costo per busta su CM (trend, low accanto) e su CT.

È il calcolo che l'utente ha fatto a mano per Star Trek, rifatto per busta equivalente: con i prezzi
di allora la Draft Night risultava leggermente più conveniente (~3%) della combinazione Play Box +
Collector Box, anche se il box sigillato si rivende meglio.

**Implementazione (2026-10-07):**
- **Catalogo**: un solo file, `SetList.json` di MTGJSON (~12 MB), contiene tutte le espansioni con
  tutti i prodotti sigillati (4.161 al 07/10), il loro contenuto, l'id Cardmarket (`mcmId`) e l'id
  del blueprint Card Trader (`cardtraderId`). `SealedCatalogImportService` lo importa in
  `MtgjsonSets`, `SealedProducts`, `SealedProductContents` (3 s), nello stesso giro giornaliero del
  listino Cardmarket e a richiesta. Migration `AddSealedProductCatalog`.
- **Uscita** = espansione MTGJSON più i set figli (`parentCode`): Star Trek comprende Star Trek
  Commander e Stardates.
- **Scomposizione** (`SealedProductAnalysisService.Resolve`): ogni prodotto si riduce a buste per set
  e tipo (es. `TRK:play`), anche su più livelli (case → box → busta). Mazzi e carte specifiche
  rendono il prodotto "a contenuto fisso"; terre, dadi, scatole sono extra. I "Land Pack" dei bundle,
  che MTGJSON registra come mazzo, sono trattati da extra.
- **Prezzo di riferimento per busta** = il €/busta più basso (trend CM) fra i prodotti fatti di un
  solo tipo di busta. **Δ vs buste** = prezzo del prodotto rispetto alle sue buste a quel prezzo;
  calcolato solo per i prodotti fatti di buste (più extra).
- **Prezzi Card Trader** a richiesta (pulsante "Prezzi Card Trader"): una chiamata al marketplace per
  espansione CT coinvolta, minimo in inglese salvato sul prodotto (`CtMinPrice`, `CtOfferCount`).
  Le offerte senza lingua indicata sono accettate: non è verificato che CT la riporti sui sigillati.
- Pagina **"Acquisti"** nel menu, endpoint `api/purchasing/*`.
- **Provato su SQL Server** con un database a parte (poi cancellato). La prova ha trovato codici busta
  MTGJSON lunghi fino a 80 caratteri e nomi di extra fino a 203: colonne dimensionate di conseguenza.
  Primi risultati: Star Trek — Draft Night +6,9% rispetto alle sue buste, Bundle +31,5%, buste
  sciolte +22/26% rispetto ai box; Reality Fracture — Draft Night −5,0%.
- **Pubblicata e verificata il 2026-10-07**: catalogo importato in produzione al primo avvio (4.161 prodotti), pagina "Acquisti" funzionante.
- **Ancora da verificare**: il pulsante "Prezzi Card Trader" non è ancora stato usato in produzione (nessun prodotto con `CtPriceUpdatedAt` al 07/10).

**Criteri di accettazione:**
- [x] Per un'espansione si vedono tutti i suoi prodotti sigillati con contenuto, prezzo CM (trend + low), prezzo CT e costo per busta equivalente
- [x] I prodotti con contenuto mancante su MTGJSON sono segnalati, non scartati in silenzio ("Contenuto non indicato da MTGJSON" / "non scomponibile")
- [ ] Il contenuto si può correggere a mano se MTGJSON è incompleto — **rimandato**: sui dati reali non è servito, il catalogo copre tutti i prodotti Star Trek. Da riprendere se capita un caso concreto

### Fase 2 — Valore atteso dell'apertura e "aprire o tenere sigillato"

- **Prodotti a contenuto fisso** (Commander, Scene Box, mazzi): valore = somma dei prezzi delle carte
  contenute. Qui pesano le carte esclusive del prodotto.
- **Buste**: per ogni configurazione e foglio di stampa, probabilità × prezzo della carta, dai pesi
  MTGJSON. Una sola chiamata al marketplace CT per espansione (come fa già `ExpansionAnalyticsService`).
- **Prezzo di vendita**: mercato CT, riportato alla scala venditore come fa l'autopricer. **Bulk**
  (sotto una soglia configurabile) e token al prezzo reale del bulk, pesato per la percentuale che se
  ne vende (dalle vendite delle espansioni passate, poi dalla Fase 5).
- Si tolgono commissioni e sovrapprezzo CT.
- Confronto con il prezzo del sigillato: **apri** se il valore atteso netto supera il prezzo di
  rivendita del sigillato, **tieni chiuso** altrimenti; più il punto di pareggio.

Sostituisce il ROI Box% di oggi (valore medio × numero di carte, `ExpansionsController`), che non
pesa le rarità e conta il bulk come se si vendesse tutto al valore medio.

**Criteri di accettazione:**
- [ ] Valore atteso per prodotto, con dettaglio per slot/rarità che spiega da dove viene il numero
- [ ] Indicazione "apri / tieni chiuso" con margine
- [ ] Se manca la composizione delle buste (es. espansione in preordine), lo si dice esplicitamente invece di stimare in silenzio

### Fase 3 — Opportunità su espansioni già uscite

Classifica di tutti i prodotti sigillati per rapporto valore atteso / prezzo d'acquisto CM, filtrabile
per tipo di prodotto ed età dell'espansione, con l'andamento del prezzo dalla Fase 0.

### Fase 4 — Avvisi

Regole valutate dopo ogni import giornaliero, ad esempio "Play Box di TRK sotto 130 € di trend",
"rapporto valore atteso / prezzo sopra 1,2", "prezzo sceso del X% in 7 giorni". Notifica nell'app
(elenco + indicatore in barra di stato, come per l'avanzamento dell'autopricer) ed email via SMTP.
Le credenziali SMTP le imposta l'utente nella configurazione di produzione.

### Fase 5 — Resa reale delle aperture e quanti box

- Registro degli acquisti: prodotto, quantità, prezzo pagato, data di acquisto e di apertura, Tag
  dell'apertura. Più avanti il registro può proporre il Tag nella maschera "Nuovo Prodotto".
- Dalle inserzioni con quel Tag: venduto, invenduto, tempi di vendita → **fattore di correzione**
  fra valore atteso e resa reale, per tipo di busta.
- Tetto su **quanti box**: quante carte di un'espansione si riescono davvero a vendere in N settimane.
- Report per Tag raggruppato per prefisso espansione.

### Secret Lair — capitolo a parte, da discutere (annotato 2026-10-07)

L'utente compra regolarmente anche **Secret Lair**, ma non su Cardmarket: direttamente dal sito
Wizards (`https://secretlair.wizards.com/eu/`) al momento del drop, più o meno ogni due settimane,
perché il margine di solito è molto buono. Il caso è diverso dagli altri prodotti: il prezzo
d'acquisto è il listino Wizards e la finestra è quella del drop, quindi la domanda è "quale drop
conviene", più che "quando comprare". Da progettare a parte su richiesta dell'utente.

Già disponibile: l'import della Fase 0 salva ogni giorno anche i circa 890 prodotti Secret Lair del
catalogo Cardmarket (categoria "MtG Set"), quindi lo storico di rivendita dei drop si accumula da
subito.

---

## 5. Modello Dati (bozza, da confermare fase per fase)

| Entità | Campi principali | Fase |
|--------|------------------|------|
| `CardmarketProduct` | `IdProduct` (PK, id CM), `Name`, `IdCategory`, `CategoryName`, `IdExpansion`, `DateAdded` | 0 |
| `CardmarketPriceSnapshot` | `IdProduct`, `Date`, `Trend`, `Low`, `Avg`, `Avg1`, `Avg7`, `Avg30`, `TrendFoil`, `LowFoil` — PK (`IdProduct`, `Date`) | 0 |
| `CardmarketImportLog` | `StartedAt`, `SourceLastModified`, `Outcome`, `RowsWritten`, `Error` | 0 |
| `SealedProduct` | `MtgjsonUuid`, `SetCode`, `Name`, `Category`, `Subtype`, `CardmarketId`, `BlueprintId` (CT), `ReleaseDate` | 1 |
| `SealedProductContent` | prodotto padre, tipo (busta / sigillato / mazzo / carta / altro), riferimento, quantità | 1 |
| `BoosterSheet` / `BoosterConfig` | composizione delle buste da MTGJSON, con i pesi | 2 |
| `AlertRule` / `Notification` | regola, soglia, canale; notifica emessa, letta | 4 |
| `ProductPurchase` | prodotto, quantità, prezzo pagato, data acquisto, data apertura, Tag | 5 |

---

## 6. Note Tecniche

- **Orario dell'import**: il listino CM viene rigenerato intorno all'01:00 italiana. Programmare
  l'import la mattina (es. 07:00) e saltarlo se `Last-Modified` non è cambiato.
- **Nessun impatto sul limite Card Trader**: Fase 0 e 1 non chiamano CT. La Fase 2 sì (una chiamata
  marketplace per espansione) e deve passare dal rate limiter condiviso.
- **Non usare `Environment.Exit`** come i servizi one-shot esistenti: l'import è un servizio
  ricorrente dentro il servizio Windows.
- **Il listino CM non distingue la lingua**: va bene per i sigillati (l'utente compra in inglese) e
  per le singole lo usiamo solo come riferimento di acquisto; il valore di vendita viene da CT,
  filtrato per lingua.
- **MTGJSON può essere incompleto** sui set appena usciti: ogni dato mancante va mostrato come tale.
