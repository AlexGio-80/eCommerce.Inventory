# eCommerce.Inventory - Richieste di Implementazione

> Documento di progetto della feature. L'analisi manuale fatta dall'utente per l'uscita di Star Trek,
> da cui la feature nasce, è in `Features/004-ProductBuying.md` sul computer dell'utente, **escluso dal
> repository** (`.gitignore`): contiene i suoi acquisti con prezzi e venditori, e il repository è pubblico.

---

## 1. Descrizione

**Titolo feature:** `Analisi acquisto prodotti sigillati`
**Priorità:** Alta
**Stato (2026-10-07):** tutte le fasi (0-5) in produzione e verificate, più il piano d'acquisto su Card
Trader e le regole di avviso generiche. Restano da fare dall'utente: configurare l'email degli avvisi e
completare il registro acquisti (vedi ROADMAP). Secret Lair: progettato il 2026-10-08 in tre fasi (vedi sezione Secret Lair).

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
- **Prezzi Card Trader verificati il 2026-10-07** su The Hobbit: 18/20 sigillati con offerte, 2 chiamate, valori coerenti con Cardmarket (Play Box 138,36 € contro 147,81 € di trend).

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

**Decisioni prese con l'utente (2026-10-07):**
- Prezzo delle singole: **Cardmarket e Card Trader affiancati** (due colonne), per vedere quanto
  differiscono prima di sceglierne uno.
- **Bulk venduto ricavato dalle vendite reali**, non stimato: copie vendute a ≤ soglia contro copie in
  vendita a ≤ soglia, solo sulle espansioni aperte all'uscita (prima carta in vendita fra 15 giorni
  prima e 45 dopo l'uscita). Al 07/10 circa il 35% (The Hobbit 50%, Lorwyn Eclipsed 40%, Avatar 37%,
  TMNT 35%, Strixhaven 30%, Reality Fracture 26%, Marvel 25%); le espansioni vecchie comprate come
  collezioni stanno fra il 4% e il 15% e sono escluse.
- **Costi di vendita 15%**. La commissione reale di Card Trader, misurata su 2.973 ordini, è il 5,56%:
  il resto è spedizione, imballaggio e lavoro. Mostrata accanto al parametro.

**Implementazione (2026-10-07):**
- **Dati MTGJSON per uscita** (`MtgjsonSetDetailImportService`): carte (con `mcmId` e `scryfallId`),
  composizione delle buste (configurazioni con pesi, fogli di stampa con le carte e i pesi) e mazzi,
  dai file dei singoli set dell'uscita più quelli da cui provengono i mazzi. Ogni giorno per le uscite
  fra 60 giorni fa e 120 giorni da oggi (finché manca la composizione delle buste, poi settimanale), e
  a richiesta ("Scarica dati delle buste"). Le carte di una Collector Booster dello Hobbit stanno in
  gran parte nel set figlio "The Hobbit Eternal": per questo si caricano tutti i set dell'uscita.
- **Ultimo prezzo Cardmarket di tutti i prodotti** (`CardmarketLatestPrices`, ~128.000 righe), dallo
  stesso import giornaliero: serve per le espansioni le cui singole non sono nello storico. Si scrive
  solo ciò che cambia. Alla prima pubblicazione si riempie anche se il listino del giorno è già importato.
- **Prezzi Card Trader delle singole** con lo stesso pulsante "Prezzi Card Trader": media delle tre
  offerte più basse in inglese e Near Mint, separatamente foil e non foil. Carte collegate ai
  blueprint tramite id Scryfall.
- **Calcolo** (`OpeningValueCalculator`): per una busta, Σ configurazioni (probabilità) × Σ slot ×
  valore medio del foglio (Σ probabilità × prezzo); per mazzi e carte fisse la somma dei prezzi. Sotto
  soglia una carta vale prezzo bulk × quota venduta. Carte senza prezzo abbassano la **copertura**
  invece di contare zero in silenzio.
- **Decisione** su Cardmarket: "Apri" se il valore atteso netto supera il ricavato netto della rivendita
  del sigillato, altrimenti "Tieni sigillato". **"Dati incompleti"** se manca la composizione di una
  busta o un mazzo, o la copertura è sotto il 90%. **"Prezzo CM dubbio"** se il trend di un prodotto
  fatto solo di altri sigillati è fuori dal 60-160% della somma dei loro prezzi: MTGJSON a volte
  abbina al case l'id Cardmarket del prodotto sbagliato (Scene Box Case da 4 box sullo "Scene Box Set"
  da 2; Gift Bundle Case sul Gift Bundle singolo).
- **Pagina**: parametri modificabili (soglia, prezzo e quota del bulk, costi), riquadro per tipo di
  busta con valore netto CM/CT e, al clic, il dettaglio per foglio e le dieci carte che pesano di più;
  colonne Apri (CM), Apri (CT), Sigillato netto, Resa apertura, Decisione.
- **Provato su SQL Server** con un database a parte (poi cancellato), su dati veri. The Hobbit: Play
  2,90 € lordi a busta (rara/mitica 1,91 €), Collector 45,35 €, Box Topper 19,45 €, copertura 100%;
  Play Box −38,8% aprendo, Collector Box −18,4%, Scene Box "Treasures of Smaug" +25,2%. Reality
  Fracture: Commander Deck +19,9%, box tutti in perdita. Star Trek: "Dati incompleti" su tutto, come
  atteso (composizione non ancora pubblicata).

**Pubblicata e verificata il 2026-10-07**: al primo avvio l'import Cardmarket ha caricato i 128.085 ultimi prezzi ("già nello storico: caricati solo gli ultimi prezzi") e l'aggiornamento giornaliero ha scaricato i dati delle buste di 4 uscite (Reality Fracture, The Hobbit, Mystery Booster Commander, The Zeta Set; Star Trek senza composizione, come atteso); i prezzi Card Trader delle singole si sono caricati (783 prezzi).

**Da tenere presente:** ai prezzi di oggi il modello dice "tieni sigillato" per tutti i box delle
ultime uscite, mentre le aperture più vecchie dell'utente sono in attivo (Strixhaven +242 €, Lorwyn
Eclipsed +375 €, TMNT +58 € nella vista `ExpansionsROI`, che non conta nemmeno le carte ancora in
vendita). È proprio la taratura della Fase 5: probabilmente l'utente compra sotto il trend e vende
sopra, e il trend CM delle singole subito dopo l'uscita non è il prezzo a cui vende nel tempo.

**Criteri di accettazione:**
- [x] Valore atteso per prodotto, con dettaglio per slot/rarità che spiega da dove viene il numero
- [x] Indicazione "apri / tieni chiuso" con margine
- [x] Se manca la composizione delle buste (es. espansione in preordine), lo si dice esplicitamente invece di stimare in silenzio

### Fase 3 — Opportunità su espansioni già uscite

Classifica di tutti i prodotti sigillati per rapporto valore atteso / prezzo d'acquisto CM, filtrabile
per tipo di prodotto ed età dell'espansione, con l'andamento del prezzo dalla Fase 0.

**Implementazione (2026-10-07):**
- **Volume**: 268 uscite hanno sigillati con un prezzo Cardmarket (2.609 prodotti); i file MTGJSON
  pesano 3-5 MB per set e un'uscita ne ha 2-4, per un totale di 1-2 GB. Per questo i dati delle buste
  si scaricano **a lotti**: ogni giorno il worker ne scarica `Purchasing:DetailImportBatchSize` (60)
  fra le uscite che non li hanno, partendo dalle più recenti (`ImportPendingAsync`): copertura completa
  in circa 5 giorni. Dopo ogni set il tracciamento EF viene azzerato, altrimenti le righe salvate si
  accumulerebbero in memoria per tutto il lotto.
- **Classifica giornaliera** (`SealedOpportunityService`, tabella `SealedOpportunities`, migration
  `AddSealedOpportunities`): dopo gli import, valore atteso, sigillato netto, resa e decisione di ogni
  prodotto di ogni uscita con i dati delle buste, una riga per prodotto e giorno. Si salva lo storico:
  la variazione del valore atteso a 7 e 30 giorni, accanto a quella del prezzo del sigillato, è il dato
  che serve per "quando comprare". L'analisi in serie carica catalogo e parametri una volta sola
  (`AnalyzeManyAsync`).
- Scheda **"Opportunità"** nella pagina Acquisti: filtri per decisione (predefinito: solo "Apri"), tipo
  di prodotto, resa minima, resa massima (predefinita 300%: oltre sono quasi sempre prezzi Cardmarket
  di riempimento a 0,02 € su prodotti vecchi non più in vendita) e copertura minima (predefinita 90%), clic su una riga per l'analisi completa
  dell'uscita, pulsante per ricalcolare subito.
- **Provato su SQL Server** su una copia ripristinata del backup (poi cancellata): lotto di 20 uscite in
  33 s con 200 MB di memoria (23.775 carte, 60.014 carte nei fogli, 410 mazzi), classifica in 4 s
  (293 prodotti: 125 "Apri", 128 "Tieni sigillato", 38 "Dati incompleti", 2 "Prezzo CM dubbio").

**Pubblicata e verificata il 2026-10-07**: al primo avvio il worker ha scaricato i dati delle buste di 65 uscite e calcolato la classifica (755 prodotti di 63 uscite, 326 "Apri"); il clic su una riga apre l'analisi dell'uscita.

**Limite noto:** in cima alla classifica ci sono quasi solo mazzi Commander e prodotti a contenuto
fisso (MH3 Collector's Edition oltre +140%, Foundations Commander +90%). È plausibile, ma il valore
atteso presuppone di vendere tutte le carte del mazzo, comprese quelle fra 0,25 e 1 € che si vendono
lentamente: la resa reale arriva più tardi e il fattore "prezzo realizzato" è misurato sulle carte da
1 € in su. Da affinare con una misura di liquidità (quota del valore nelle carte sopra una soglia)
quando ci saranno aperture di mazzi registrate.

### Fase 4 — Avvisi

Regole valutate dopo ogni import giornaliero, ad esempio "Play Box di TRK sotto 130 € di trend",
"rapporto valore atteso / prezzo sopra 1,2", "prezzo sceso del X% in 7 giorni". Notifica nell'app
(elenco + indicatore in barra di stato, come per l'avanzamento dell'autopricer) ed email via SMTP.
Le credenziali SMTP le imposta l'utente nella configurazione di produzione.

**Implementazione (2026-10-07):**
- Tre tipi di regola (`AlertRule`): **prezzo sotto soglia** (trend o low CM di un prodotto),
  **calo di prezzo** (trend sceso di almeno X% rispetto a 7 giorni prima, dallo storico giornaliero dei
  sigillati: funziona dal 14/10/2026, una settimana dopo l'inizio dello storico), **apertura
  conveniente** (prodotto in "Apri" con resa ≥ X%, facoltativamente solo per un'uscita o una categoria).
- **Un avviso scatta quando la condizione diventa vera** (`AlertRuleMatch` tiene i prodotti per cui è
  già vera): niente avvisi ripetuti ogni giorno; se la condizione smette di essere vera e poi lo
  ridiventa, l'avviso scatta di nuovo. Cambiando la condizione di una regola lo stato riparte da zero.
- Valutazione ogni mattina nel worker dopo la classifica delle opportunità, e a richiesta ("Valuta ora").
- **Email**: un unico riepilogo per giro con tutti gli avvisi nuovi, via SMTP (`SmtpEmailSender`,
  sezione `Email` della configurazione, solo in uscita). L'esito (inviata / errore) resta su ogni
  avviso. Pulsante "Email di prova".
- **Nell'app**: campanella nella barra in alto (era già presente ma inattiva) con il numero di avvisi
  non letti, aggiornata ogni 5 minuti; clic su un avviso → analisi dell'uscita. Scheda **"Avvisi"**
  nella pagina Acquisti con regole e storico degli avvisi.
- Migration `AddPurchaseAlerts`: tre tabelle nuove.
- **Pubblicata e verificata il 2026-10-07** (regole, valutazione, campanella). L'invio email resta da verificare quando l'utente avrà configurato le credenziali SMTP.

**Regole generiche (richiesta dell'utente, 2026-10-07):** inserire a mano una regola per prodotto
avrebbe voluto dire centinaia di regole. Invece di generarle in automatico, ogni regola ha un
**ambito**: un solo prodotto oppure tutti i prodotti che passano i filtri (uscita, categoria,
sottotipo, uscite degli ultimi N giorni compresi i preordini; i case sono sempre esclusi).
- Scartata la soglia fissa in euro per tipo di prodotto (es. "Collector sotto 400 €"): i 53 Collector
  Booster Box in vendita vanno da 189 € a 5.780 € (media 633 €), e i più economici costano poco perché
  valgono poco (gli otto sotto 280 € sono tutti "Tieni sigillato"). Si usano confronti relativi: con
  il valore delle carte ("apertura conveniente") o con la storia del prodotto.
- Nuovo tipo **prezzo al minimo**: trend al valore più basso degli ultimi N giorni, solo per i prodotti
  il cui storico parte prima della finestra.
- **Un avviso per regola e per giro**, con l'elenco dei prodotti (i primi 15, ordinati dal più
  interessante): con regole generiche decine di prodotti possono scattare insieme.
- **Regole predefinite** create una volta dalla migration `AddAlertRuleScopes` (se cancellate non
  ricompaiono): box con resa ≥ 15%; box delle uscite degli ultimi 180 giorni in calo ≥ 10% in 7 giorni;
  box delle uscite degli ultimi 365 giorni al minimo di 90 giorni. Al 07/10 la prima scatta su 17 box
  (in cima Jumpstart e Theme: restringere il sottotipo se interessano solo Play e Collector); il calo a
  7 giorni funziona dal 14/10, il minimo a 90 giorni da gennaio 2027.
- **Pubblicate e verificate il 2026-10-07**: al primo giro un unico avviso con 17 box.

**Piano d'acquisto su Card Trader (richiesta dell'utente, 2026-10-07):** comprare da uno stesso
venditore più prodotti convenienti per risparmiare sulla spedizione.
- **Cardmarket non è utilizzabile**: il listino pubblico ha solo prezzi aggregati, senza venditori; le
  offerte per venditore richiederebbero l'API (chiusa) o la lettura delle pagine (esclusa). Per
  Cardmarket resta lo "Shopping Wizard" del sito: si mettono i prodotti in una lista dei desideri e il
  sito trova la combinazione di venditori più economica, spedizioni comprese.
- **Card Trader sì**: il marketplace dà le offerte per venditore, con paese e possibilità di spedire i
  sigillati con **Card Trader Zero** (`CanSellSealedWithCtZero`), che riunisce in un'unica spedizione
  articoli di venditori diversi.
- `PurchasePlanService`: per i prodotti filtrati nella scheda Opportunità (al massimo 40), una chiamata
  al marketplace per prodotto (limitatore condiviso: circa 3 s a prodotto); tiene le offerte in inglese
  a cui aprire il prodotto conviene (resa calcolata sul prezzo reale dell'offerta, non sul trend), le
  raggruppa per venditore (per ogni prodotto la sua offerta più economica) e costruisce il carrello CT
  Zero (per ogni prodotto l'offerta CT Zero più economica).
- Scelte dell'utente: parte dalla classifica con i filtri impostati; **solo a richiesta** (pulsante
  "Piano d'acquisto" nella scheda Opportunità), perché le offerte cambiano di ora in ora.
- Spedizione esclusa dai conti: Card Trader la calcola al carrello.
- **Pubblicato e verificato il 2026-10-07.**

**Evoluzione delle regole:** le tre regole predefinite sono un punto di partenza. Ogni lunedì alle
08:00 un'attività programmata di Claude (app desktop) legge in sola lettura avvisi, classifica e
acquisti e scrive in `Revisioni-avvisi/` (locale, escluso da git) un rapporto con le regole da rivedere
e quelle nuove proposte; si aggiungono solo dopo l'ok dell'utente.

**Email configurata e verificata il 2026-10-07.** Per riconfigurarla: in `appsettings.Production.json`, sezione `Email` (già
presente, vuota e disattivata): `UserName` e `From` = indirizzo Gmail, `Password` = una **password
per le app** creata su myaccount.google.com/apppasswords (richiede la verifica in due passaggi; non è
la password dell'account), `To` = dove ricevere gli avvisi, `Enabled` = true. Poi pubblicare e
premere "Email di prova" nella scheda Avvisi.

### Fase 5 — Resa reale delle aperture e quanti box

- Registro degli acquisti: prodotto, quantità, prezzo pagato, data di acquisto e di apertura, Tag
  dell'apertura. Più avanti il registro può proporre il Tag nella maschera "Nuovo Prodotto".
- Dalle inserzioni con quel Tag: venduto, invenduto, tempi di vendita → **fattore di correzione**
  fra valore atteso e resa reale, per tipo di busta.
- Tetto su **quanti box**: quante carte di un'espansione si riescono davvero a vendere in N settimane.
- Report per Tag raggruppato per prefisso espansione.

**Cosa è emerso dai dati (2026-10-07, su una copia ripristinata del backup):**
- **Prezzo realizzato**: sulle carte da 1 € in su, negli ultimi 30 giorni l'utente ha incassato il
  **120% del trend Cardmarket** del momento (+29% fra 1 e 3 €, +16-17% sopra i 3 €). Il confronto
  con vendite più vecchie gonfierebbe il rapporto, perché il trend delle singole cala dopo l'uscita.
  Sotto 1 € l'abbinamento automatico CT↔CM (`CardMarketIds[0]`) è rumoroso e le carte sono comunque
  governate dalle regole del bulk.
- **Le modifiche fatte dalla maschera sono contate due volte**: in una modifica (`IsUpdate`) la quantità
  è il nuovo totale dell'inserzione (`PendingListingsController` imposta `localItem.Quantity =
  pending.Quantity`), non le copie aggiunte. Per Marvel 458 modifiche portano 7.576 copie e 2.576 €
  di costo, ma le copie davvero aggiunte sono 1.228 (417 €). Il bilancio della Fase 5 conta solo le
  copie aggiunte; **il report di redditività esistente (vista `ExpansionsROI`) ha lo stesso difetto**
  e sovrastima i costi (Marvel 3.674 € invece di circa 2.040 €). **Corretto il 2026-10-07**: il costo viene da
  `PurchaseCostService`, unico per report e bilancio aperture (vedi CHANGELOG).

**Decisioni prese con l'utente (2026-10-07):**
- Il fattore "prezzo realizzato" si applica **in automatico** al valore atteso su prezzi Cardmarket
  (carte sopra la soglia del bulk), misurato e modificabile dalla pagina. Non si applica ai prezzi
  Card Trader, che sono già il mercato su cui si vende.
- Gli acquisti già fatti per Star Trek si **precaricano** nel registro (i box di cui l'utente
  ha indicato quantità e prezzo; gli altri prodotti li completa l'utente). Scrittura
  diretta sul database di produzione dopo la pubblicazione, con conferma: non in una migration, perché
  il repository è pubblico e conterrebbe i prezzi d'acquisto.
- "Quanti box": per ora la curva di incasso per apertura; una regola automatica si ricava quando ci
  saranno aperture registrate con la previsione.

**Implementazione (2026-10-07):**
- `PriceRealizationService`: incassato / trend CM sulle vendite degli ultimi 30 giorni, carte con trend
  ≥ 1 €; con meno di 100 € di trend venduto si usa 1. Nuovo parametro `PriceFactor` in
  `OpeningValueSettings`, applicato solo sopra soglia.
- `OpeningBalanceService`: bilancio per tag (normalizzato: `#` e maiuscole non contano), copie e costo
  dai caricamenti contando delle modifiche solo le copie aggiunte (quantità precedente dallo storico
  prezzi o dal caricamento precedente), vendite solo da un giorno prima del primo caricamento (il
  recupero dei tag ha attribuito alcuni tag a ordini di anni prima), incasso netto con la commissione
  misurata, curva a 30/60/90/180 giorni, indicazione "aperta all'uscita" (primo caricamento entro 45
  giorni dall'uscita). Esclusi i tag sotto 20 copie o 50 € (lotti di vecchie collezioni).
- `ProductPurchase` + `ProductPurchaseService`: registro acquisti, con la previsione del modello
  salvata alla registrazione e ricalcolata quando si segna l'apertura; vuota se i dati non bastano.
  Migration `AddProductPurchases`.
- Pagina "Acquisti" a schede: **Analisi uscita** (con il parametro "Prezzo realizzato %" e il
  pulsante per registrare un acquisto dalla riga), **Aperture** (bilancio), **Registro acquisti**
  (con tag proposto nel formato `CODICE_TIPO_AAAAMMGG`).
- **Provato su SQL Server** su una copia ripristinata del backup del 07/10 (poi cancellata): fattore
  120,2% su 246 copie; The Hobbit con il fattore: Collector Box "Apri" (575,58 € netti aprendo contro
  499,25 € rivendendola chiusa), Play Box ancora "Tieni sigillato" (−27,2%); Lorwyn Eclipsed: Commander
  Deck e Theme Deck "Apri". Bilancio: The Hobbit 1.926 € di costo, 822 € incassati in 58 giorni;
  Marvel 2.040 € di costo, 1.483 € incassati in 102 giorni.
- **Pubblicata e verificata il 2026-10-07.** Precaricati nel registro, con scrittura diretta in
  produzione autorizzata dall'utente: i box Star Trek Play e Collector già
  acquistati, data d'acquisto 07/10/2026, senza
  previsione (composizione delle buste non ancora pubblicata: si calcolerà all'apertura). Commander
  Deck Set e Scene Box Set da aggiungere dall'utente, prezzi non noti.

### Secret Lair (progettato il 2026-10-08)

L'utente compra regolarmente anche **Secret Lair**, ma non su Cardmarket: direttamente dal sito
Wizards (`https://secretlair.wizards.com/eu/shopall`) al momento del drop, più o meno ogni due
settimane, perché il margine di solito è molto buono. Li vende **sempre aperti, a singole**. Il prezzo
d'acquisto è il listino Wizards e la finestra è quella del drop, quindi la domanda è "quale drop
conviene", più che "quando comprare".

**Dati di partenza (verificati l'08/10/2026):**

- Vendite Secret Lair dal novembre 2025: circa 400 copie negli ordini pagati (gli ordini `hub_pending`
  di Card Trader Zero sembrano duplicati, vedi ROADMAP); in vendita 618 copie. Tutte con il tag
  `#SLD_OLD`: il bilancio per drop non si legge dal tag
- Catalogo MTGJSON: 915 prodotti Secret Lair, 807 con il contenuto (carte o mazzi), 383 con un trend
  Cardmarket da sigillato. Tipi: drop normale (~415), drop foil (~363, compresi "Rainbow Foil"),
  bundle dei superdrop (123), mazzi Commander (9)
- Abbinamento carta venduta → drop passando da carte e mazzi MTGJSON (id Scryfall del blueprint):
  **circa il 90% dell'incasso**, 135 drop. Solo dalle carte, senza i mazzi, l'8%: in
  MTGJSON la maggior parte dei drop è descritta come mazzo
- Sito Wizards: catalogo e pagine prodotto in HTML, senza interfaccia dati. La pagina "Shop all" ha
  id prodotto, nome, prezzo, foil, stato (preordine, scorte basse, disponibile); la pagina del prodotto
  ha **l'elenco delle carte già prima dell'uscita** (nome reale della carta), data di spedizione e
  limite per cliente. `robots.txt` non vieta queste pagine. Il sito usa Queue-it nei momenti di picco

**Decisioni (utente, 08/10/2026):**

| Decisione | Scelta |
|-----------|--------|
| Prezzo pagato per la retrospettiva | Standard per tipo, correggibile drop per drop dal registro acquisti: **34,99 €** drop normale, **44,99 €** foil, **149 €** bundle, **179 €** mazzo Commander |
| Vendita | Sempre aperti, a singole: si confronta con l'incassato dalle singole, il sigillato non conta |
| Tag dei prossimi drop | Uno per drop, `#SLD_<codice>_AAAAMMGG`, proposto dal registro acquisti |
| Monitoraggio del sito Wizards | Sì, per suggerire i drop da comprare |

**Fasi:**

1. **Retrospettiva per drop** (pubblicata e verificata il 2026-10-08) — scheda "Secret Lair" della pagina Acquisti. Per ogni drop venduto o in
   vendita: tipo, copie (dalle carte caricate: il massimo di copie fra le carte del drop), prezzo
   unitario (registro acquisti, altrimenti standard), spesa, incassato e valore in vendita al netto
   della commissione Card Trader misurata, resa, quota già rientrata, valore di oggi delle singole su
   Cardmarket. Riepilogo per tipo. Le carte si abbinano al drop per id Scryfall e foil, passando da
   carte e mazzi MTGJSON; i bundle non si abbinano (le carte vanno ai drop che contengono). Per i drop
   con tag dedicato vale il tag. **Drop comprato intero** = tutte le carte caricate (almeno 3 diverse) o
   registrato: le carte dei drop incompleti sono singole arrivate con lotti o scambi (utente, 08/10) e
   stanno in un riepilogo a parte. Copie = minimo di copie fra le carte del drop. Primo risultato: 86 drop,
   resa con le copie in vendita +91% (normali +102%, foil +34%)
2. **Monitoraggio del sito Wizards** — due-tre letture al giorno della pagina "Shop all" e delle sole
   pagine prodotto nuove o cambiate. Si registrano prodotto, prezzo, foil, stato, carte, data di
   comparsa e di esaurimento. Avviso (campanella ed email) per ogni drop nuovo. Se la lettura non
   riesce (coda Queue-it), si salta il giro. Da qui anche il **link al sito Wizards** nella scheda: il
   negozio mostra solo i drop in vendita, l'id Wizards non è in MTGJSON e la ricerca del sito è vietata
   da `robots.txt`, quindi il link c'è solo per i drop visti dal monitoraggio
3. **Valutazione del drop prima dell'acquisto** — per ogni carta del drop, il prezzo delle stampe già
   esistenti (Cardmarket e Card Trader) per il sovrapprezzo Secret Lair misurato sui drop passati
   (versione Secret Lair contro stampa più economica, separato foil / non foil), con quota venduta e
   costi: confronto con il prezzo Wizards e suggerimento "Compra" / "Lascia" nella scheda e negli
   avvisi. Le carte mai stampate prima usano la media dei drop simili e sono segnalate come stima
   debole. Dopo l'uscita i prezzi reali sostituiscono la stima e si misura quanto ci aveva preso

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
