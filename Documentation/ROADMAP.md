# eCommerce.Inventory - Roadmap

> Aggiornare questo file a ogni sessione: spostare le voci tra le sezioni man mano che il lavoro avanza.

---

## In Corso

- [x] **Secret Lair, Fase 3: valutazione del drop prima dell'acquisto (2026-10-09)** — pubblicata e verificata dall'utente il 2026-10-09. Verifica: scheda Secret Lair → "Negozio Wizards": colonne Stima / Valore netto / Margine; su "Masters of the Universe: By the Power of Grayskull!" circa 49 € netti contro 34,99 € (Compra), tooltip del valore con stampa base e prezzo SL carta per carta; dopo la prima lettura del negozio la colonna "Stima prima dell'uscita" si riempie per i drop non ancora usciti. Al prossimo drop nuovo l'avviso deve riportare "Stima: valore netto …". Dopo l'uscita dei MOTU (03/11) guardare la colonna "Scarto stima"
- [x] **Secret Lair, Fase 2: monitoraggio del negozio Wizards (2026-10-08)** — pubblicata e verificata dall'utente il 2026-10-08; restano da vedere nei giorni seguenti il completamento delle carte e il primo avviso su un superdrop nuovo. Verifica: scheda Secret Lair → "Negozio Wizards" → "Leggi ora": la prima lettura registra ~258 prodotti senza avvisi e legge le carte di 30; nei giorni seguenti le carte si completano e ogni superdrop nuovo produce un avviso (campanella ed email). Se una lettura risulta "fallita", l'interfaccia StoreSearch potrebbe essere cambiata
- [x] **Secret Lair, Fase 1: retrospettiva per drop (2026-10-08)** — pubblicata e verificata dall'utente il 2026-10-08, con link CT e CM: scheda "Secret Lair" nella pagina Acquisti. Verifica: 86 drop circa, riepilogo normale/foil con resa +102% / +34%, link CM funzionanti; tag proposto `#SLD_...` nel registro acquisti su un prodotto Secret Lair
- [ ] **Articoli da preparare: l'ordine settimanale Card Trader Zero eredita la preparazione giornaliera (2026-10-09)** — pubblicato il 2026-10-09, da verificare al prossimo ordine raccolto. Alla prima sincronizzazione degli ordini dopo la pubblicazione si abbina lo storico (~16.000 righe, pochi secondi, nessun flag cambiato). Verifica: al prossimo ordine "Ct connect" raccolto, in "Articoli da preparare" devono restare solo le carte non preparate durante la settimana, senza UPDATE manuale; nel log della sincronizzazione la riga "Card Trader Zero: N righe degli ordini raccolti abbinate…"
- [x] **Ordini `hub_pending` di "Ct connect": corretti il 2026-10-09, pubblicati e verificati dall'utente** — erano doppioni degli ordini "Ct connect" pagati; li contavano la vista `ExpansionsROI` (venduto per espansione doppio sulle uscite recenti) e la quota venduta del valore atteso (bulk dal 35% al 21%). Verifica: pagina Espansioni / report per espansione, Secrets of Strixhaven venduto ~467 € (prima 935 €); scheda Opportunità → "Ricalcola", parametri con bulk venduto ~21%. Voce originale: (trovati il 2026-10-08) 12.484 ordini senza data di pagamento, il 97% delle righe ha una gemella identica fra gli ordini pagati. Probabili duplicati di Card Trader Zero: controllare quali report li sommano
- [x] **Valore atteso: costo per carta, quota venduta per fascia, prodotti non giocabili (2026-10-08)** — pubblicato e verificato dall'utente il 2026-10-08. Dopo la pubblicazione premere "Ricalcola" nella scheda Opportunità per rifare la classifica di oggi; i World Championship Deck devono risultare "Non giocabili". Il costo per carta (0,15 €) è una stima dell'utente: da rivedere con l'esperienza
- [x] **Autopricer: carte appena caricate al prezzo di mercato (2026-10-08)** — pubblicato e verificato dall'utente il 2026-10-08. Una carta mai prezzata dall'autopricer ha la fascia ricalcolata sul prezzo proposto e nessun guardrail. Verifica: dopo un caricamento dalla maschera, nella scheda Esecuzioni (origine "Nuova inserzione") le carte nuove devono risultare applicate con la motivazione "Inserzione nuova", non più `BlockedByGuardrail`
- [x] **Autopricer: regole a ripiego (2026-10-08)** — pubblicate e configurate l'08/10 (ripiego su tutti i venditori, min 2 fino a 25 €, min 3 sopra), **verificate sulla notturna del 09/10**: esiti "poche offerte" da 316–364 a 112 per notte, 306 valutazioni con "Regola di ripiego" (124 applicate), variazioni plausibili sopra 25 €, guardrail scattato su 3 carte invece di 8–82. Più regole sulla stessa fascia diventano una catena; ogni regola può avere minimo di offerte e venditori propri (pulsante ↳ sulla riga nella scheda Regole)
- [ ] **Bulk `#FRA_OLD`/`#HOB_OLD` bloccato dal guardrail il 07/10: ricontrollare dopo il 16/10** — 169 carte hanno come ultimo esito `BlockedByGuardrail` e non sono mai state prezzate, quindi al prossimo passaggio saranno trattate come carte nuove (niente guardrail). Non sono state rivalutate il 09/10 perché il bulk (≤ 1 €) gira a rotazione, 2.000 blueprint a notte (`AutoPricing:BulkSliceSize`), ciclo di circa 9 giorni. In alternativa basta un'esecuzione manuale
- [x] **Piano d'acquisto: colonna "Trend CM" e link "CM" accanto alle offerte Card Trader (2026-10-08)** — pubblicato e verificato il 2026-10-08: per confrontare l'offerta Card Trader con il prezzo dello stesso prodotto su Cardmarket
- [x] **Fix Analisi uscita di Secret Lair (2026-10-08)** — pubblicato e verificato il 2026-10-08: le centinaia di schede del valore per busta (ogni drop è una "busta" per MTGJSON) nascondevano la griglia. Ora se ne vedono 10 con "Mostra tutte le buste", e la griglia ha un'altezza minima
- [x] **Prezzi Card Trader automatici anche aprendo un'uscita dalla scheda Opportunità (2026-10-08)** — pubblicato e verificato il 2026-10-08: stessa regola degli avvisi (solo se mancano o hanno più di 6 ore)
- [x] **Analisi uscita: prezzi Card Trader aggiornati in automatico arrivando da un avviso, e link a Cardmarket (2026-10-08)** — pubblicato e verificato dall'utente il 2026-10-08. Dalla campanella o dalla scheda Avvisi, se i prezzi Card Trader dell'uscita mancano o hanno più di 6 ore parte da solo "Prezzi Card Trader" (fra 2 e 6 chiamate). Nuova colonna "CM" accanto a "CT": apre la ricerca di Cardmarket col nome esatto del prodotto su Cardmarket (non esiste un indirizzo per id prodotto). La ricerca porta al prodotto giusto
- [x] **Copia del valore di una cella con doppio clic, in tutte le griglie (2026-10-08)** — pubblicata e verificata dall'utente il 2026-10-08: doppio clic su una cella (es. Tag, nome prodotto) e il valore va negli appunti, con un avviso "Copiato". Su `http://inventory.local` il browser non offre l'API moderna degli appunti: da verificare proprio lì che la copia funzioni
- [ ] **Analisi acquisto prodotti sigillati (progettata il 2026-10-07)** — progetto in `Documentation/Features/004-AnalisiAcquistoProdotti.md`, analisi manuale di partenza dell'utente in `Features/004-ProductBuying.md`. **Fase 0 pubblicata e verificata il 2026-10-07**: import giornaliero del listino Cardmarket (all'avvio + ogni giorno alle 07:00), primo import riuscito con 5.099 sigillati e 7.514 singole. Da vedere nei prossimi giorni che la serie dei prodotti Star Trek cresca di una riga al giorno (registro in `CardmarketImportLogs`). **Fase 1 pubblicata e verificata il 2026-10-07**, compreso il pulsante "Prezzi Card Trader". **Fase 2 pubblicata e verificata il 2026-10-07**: valore atteso dell'apertura e decisione apri / tieni sigillato. **Fase 5 pubblicata e verificata il 2026-10-07**: fattore "prezzo realizzato" (~120%), bilancio reale per apertura, registro acquisti; precaricati i box Star Trek (Commander Deck Set e Scene Box Set da aggiungere dall'utente). **Fase 3 pubblicata e verificata il 2026-10-07**: classifica giornaliera delle opportunità (scheda "Opportunità"), dati delle buste scaricati a lotti di 60 uscite al giorno (65 al primo avvio, copertura completa in circa 5 giorni). **Fase 4 pubblicata e verificata il 2026-10-07**: avvisi su prezzo sotto soglia, calo di prezzo e apertura conveniente, nella campanella e via email (email configurata e verificata il 2026-10-07). Tutte le fasi della feature sono in produzione. **Regole di avviso generiche pubblicate e verificate il 2026-10-07**: ambito per gruppo di prodotti, tipo "prezzo al minimo", un avviso per regola, tre regole predefinite. Le fasi sono elencate in Da Fare
- [ ] **Soglia in euro per il guardrail (2026-09-26), pubblicata; campo verificato in interfaccia, effetto da vedere nelle notturne**: nuovo campo "Variazione libera" (default 0,10 €, in produzione impostato a 0,15 €) nella sezione Guardrail; sotto quella cifra i limiti percentuali non scattano. Sblocca 195 carte di bulk ferme al −50% dopo il fix del sovrapprezzo. Da verificare: nelle notturne successive le carte di bulk con esito `BlockedByGuardrail` devono sparire, salvo ribassi oltre 0,10 €
- [ ] **Rivalutare "N-esima più bassa" contro percentile** dopo una settimana col fix: il passaggio a "N-esima" era stato fatto per recuperare le vendite del bulk, che il fix risolve alla radice. Cambiare una cosa alla volta per poter misurare

---

## Completato (Sessione 2026-10-07)

| Data | Voce |
|------|------|
| 2026-10-07 | Feature — **Analisi acquisto prodotti sigillati, Fase 0**: import giornaliero del listino pubblico Cardmarket (storico dei sigillati, singole recenti, ultimo prezzo di tutti i prodotti) |
| 2026-10-07 | Feature — **Fase 1**: pagina "Acquisti" con catalogo sigillati MTGJSON e convenienza fra formati per busta equivalente |
| 2026-10-07 | Feature — **Fase 2**: valore atteso dell'apertura (probabilità per slot da MTGJSON, prezzi CM e CT affiancati, bulk alla quota venduta misurata) e decisione Apri / Tieni sigillato |
| 2026-10-07 | Feature — **Fase 5**: fattore "prezzo realizzato" (~120% del trend CM sulle carte da 1 € in su), bilancio reale delle aperture per tag, registro acquisti con previsione |
| 2026-10-07 | Feature — **Fase 3**: classifica giornaliera delle opportunità su tutte le uscite, dati delle buste scaricati a lotti |
| 2026-10-07 | Feature — **Fase 4**: avvisi nella campanella e via email; regole generiche per gruppo di prodotti, tipo "prezzo al minimo", tre regole predefinite |
| 2026-10-07 | Feature — **Piano d'acquisto su Card Trader** per venditore e carrello Card Trader Zero |
| 2026-10-07 | Feature — **Filtro "Resa massima %"** nelle Opportunità (predefinito 300): nasconde le rese irreali da prezzi Cardmarket di riempimento |
| 2026-10-07 | Feature — **Colonne salvate in tutte le griglie** (direttiva `appGridState`); Inventario, Ordini e Articoli da preparare restano a salvataggio manuale |
| 2026-10-07 | Operativo — **Email degli avvisi** configurata e verificata; **revisione settimanale** delle regole programmata il lunedì |
| 2026-10-07 | Fix — **Report di redditività**: il costo delle modifiche fatte dalla maschera era contato due volte (Marvel 3.674 € invece di 1.587 €); costo unico da `PurchaseCostService`, giacenza per tag non più moltiplicata |

---

## Completato (Sessione 2026-09-25/26)

| Data | Voce |
|------|------|
| 2026-09-25 | Fix — **Sovrapprezzo di Card Trader sul bulk**: il motore lo scartava sotto 0,25 € (rapporto fino a 1,9, oltre il limite di 1,15) e prezzava ogni carta circa 0,09 € sopra la posizione configurata; ora lo sottrae come differenza. 26.011 inserzioni riportate a 0,05 € con `Scripts/Ripristina-PrezziBulk.ps1`. Verificato sulla notturna del 26/09: conversione riuscita su tutte le 3.459 carte sotto 0,25 €, le carte a 0,05 € restano lì |
| 2026-09-25 | Fix — **Lo storico prezzi non registrava i riprezzi dell'autopricer**: ora confronta con l'ultima rilevazione salvata. Verificato il 26/09: 8.373 rilevazioni al primo giro contro 4–40 a notte |
| 2026-09-26 | Feature — **Soglia in euro per il guardrail** ("Variazione libera", in produzione a 0,15 €): sotto quella variazione i limiti percentuali non scattano, sblocca le carte di bulk ferme al −50%. Pubblicata, effetto da vedere nelle notturne (vedi In Corso) |

---

## Completato (Sessione 2026-09-05)

| Data | Voce |
|------|------|
| 2026-09-05 | Sicurezza — **Firma obbligatoria sul webhook Card Trader**: header `X-Signature` mancante o non valido ora restituisce `401` invece di processare l'evento. Sistemato anche l'ordine `EnableBuffering()`/model binding (nuovo resource filter `EnableRequestBodyBufferingAttribute`), altrimenti la rilettura del corpo per la firma sarebbe sempre stata vuota. Aggiunti test HTTP end-to-end con `TestServer` |
| 2026-09-05 | Fix — **Header firma webhook sbagliato + shared secret mai impostato**: il controller cercava `X-Signature` invece del vero `Signature` (documentazione ufficiale Card Trader); lo shared secret di produzione era ancora un placeholder, corretto con il valore reale da `GET /info` |
| 2026-09-05 | Feature — **"Applica comunque" per bypassare il guardrail dalla scheda Storico**: nuovo parametro `bypassGuardrail` lungo tutta la catena fino a `PricingEngine.Evaluate`; nella griglia dei calcoli, solo le righe bloccate dal guardrail sono selezionabili. Verificato in produzione |
| 2026-09-05 | Fix — **Filtro per Origine nella scheda Esecuzioni**: senza, una giornata con molte "Nuova inserzione" faceva sparire la notturna oltre il limite di righe caricate. Verificato in produzione |
| 2026-09-05 | Feature — **Grafico storico prezzi in "Nuovo Prodotto"**: una linea per inserzione (condizione/lingua/foil), alimentato da `PriceHistoryEntries`. Aggiunti anche link "Vedi su Card Trader", nome italiano e numero di raccolta sotto l'immagine, colonna CT nella griglia "Coda inserzioni". Verificato in produzione |
| 2026-09-05 | Feature — **Riferimento di mercato affiancato al grafico storico prezzi**: nuovo campo `PriceChangeLog.ReferenceSellerPrice`, calcolato da `PricingEngine.Evaluate` riportando `ReferencePrice` (scala vetrina) alla scala venditore. Risolve anche il punto aperto sulla scala di `ReferencePrice` |

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

### Analisi acquisto prodotti sigillati — fasi (dettaglio in `Documentation/Features/004-AnalisiAcquistoProdotti.md`)

Si compra su Cardmarket (solo in inglese), si vende su Card Trader. Fonti: listino e catalogo pubblici
giornalieri di Cardmarket (l'API CM non accetta nuove richieste), abbinamento CT↔CM già in
`Blueprint.CardMarketIds`, contenuto dei prodotti e composizione delle buste da MTGJSON.

- [x] **Fase 0 — Import giornaliero del listino Cardmarket** — pubblicata e verificata il 2026-10-07: sigillati tutti i giorni, singole solo a variazione e solo per le espansioni comparse su Cardmarket negli ultimi 12 mesi. Nessuna chiamata a Card Trader
- [x] **Fase 1 — Contenuto dei prodotti e convenienza fra formati** — pubblicata e verificata il 2026-10-07: prodotti sigillati e contenuto da MTGJSON, costo per busta equivalente (Play / Collector) su CM (trend, low accanto) e CT
- [x] **Fase 2 — Valore atteso dell'apertura e "aprire o tenere sigillato"** — pubblicata e verificata il 2026-10-07: somma delle singole per i prodotti a contenuto fisso, probabilità per slot per le buste; bulk e token al prezzo reale pesato per quanto se ne vende; sostituisce il ROI Box% di oggi
- [x] **Fase 3 — Opportunità su espansioni già uscite** — pubblicata e verificata il 2026-10-07: classifica dei prodotti per valore atteso / prezzo d'acquisto, con andamento
- [x] **Fase 4 — Avvisi** — pubblicata e verificata il 2026-10-07 (invio email configurato e verificato il 2026-10-07 con "Email di prova"): regole valutate dopo ogni import, notifica nell'app + email (SMTP, solo in uscita)
- [x] **Fase 5 — Resa reale delle aperture e quanti box** — pubblicata e verificata il 2026-10-07 (regola automatica sul "quanti box" rimandata a quando ci saranno aperture registrate con la previsione): registro acquisti, resa dai Tag `CODICE_TIPO_AAAAMMGG`, fattore di correzione, tetto sul numero di box, report per Tag raggruppato per prefisso
- [x] **Piano d'acquisto su Card Trader (2026-10-07), pubblicato e verificato**: pulsante "Piano d'acquisto" nella scheda Opportunità; offerte convenienti raggruppate per venditore e carrello Card Trader Zero
- [x] **Report di redditività: le modifiche dalla maschera contano due volte il costo (trovato il 2026-10-07) — corretto, pubblicato e verificato il 2026-10-07**: costo da `PurchaseCostService` per pagina Espansioni, redditività per espansione, per Tag e Tag → espansione; corretta anche la giacenza per Tag, che moltiplicava le inserzioni modificate. Dettaglio nel CHANGELOG. Voce originale: — la vista `ExpansionsROI` somma `PurchasePrice × Quantity` di tutte le righe di `PendingListings`, comprese le modifiche (`IsUpdate`), la cui quantità è il nuovo totale dell'inserzione. Per Marvel il "Totale acquistato" è 3.674 € invece di circa 2.040 €. Il bilancio delle aperture (pagina Acquisti) conta già solo le copie aggiunte; il report per espansione/tag va allineato con la stessa logica (`OpeningBalanceService.AddedCopiesAsync`)
- [ ] **Secret Lair — progettato il 2026-10-08 in tre fasi** (dettaglio nel documento della feature, sezione Secret Lair): **Fase 1 retrospettiva per drop** pubblicata e verificata; **Fase 2 monitoraggio del sito Wizards** pubblicata e verificata; **Fase 3 valutazione del drop prima dell'acquisto** pubblicata e verificata il 2026-10-09; resta da confrontare la stima congelata con i prezzi reali dopo l'uscita dei MOTU (03/11). Nota originale: comprati dal sito Wizards al drop (circa ogni due settimane), margine di solito molto buono. Lo storico di rivendita su Cardmarket si accumula già con la Fase 0 (~890 prodotti). Dettaglio nel documento della feature
- [ ] **Registro acquisti: nomi diversi da Cardmarket (solo se l'utente lo chiede)** — il catalogo usa i nomi MTGJSON ("Foundations Commander Decks Set of 5" invece di "Commander: Foundations: Deck Set") e l'elenco prodotti mostra solo l'uscita aperta in "Analisi uscita" (i mazzi Commander di un'espansione stanno nell'uscita principale). Possibili rimedi: mostrare anche il nome Cardmarket e una ricerca su tutto il catalogo. Il 2026-10-07 l'utente ha scelto di andare avanti così e decidere più avanti
- [ ] **Regole di avviso da far crescere con le prove** — revisione settimanale programmata (attività di Claude nell'app desktop, lunedì 08:00): rapporto in `Revisioni-avvisi/` (locale, escluso da git) con regole da rivedere e nuove proposte; si aggiungono solo dopo l'ok dell'utente. Prima revisione utile dopo il 12/10 (classifica completa) e il 14/10 (cali a 7 giorni)
- [x] **Opportunità: filtro "Resa massima %"** (predefinito 300) — pubblicato e verificato il 2026-10-07: nasconde le rese irreali dei prodotti vecchi con prezzo Cardmarket di riempimento (0,02 €, es. Alpha Starter Deck a +42 milioni %)
- [x] **Da fare dall'utente: configurare l'email degli avvisi** — fatto il 2026-10-07, email di prova arrivata — in `appsettings.Production.json`, sezione `Email` (già presente, disattivata): `UserName` e `From` = indirizzo Gmail, `Password` = password per le app (myaccount.google.com/apppasswords, serve la verifica in due passaggi), `To` = destinatario, `Enabled` = true; pubblicare e premere "Email di prova" nella scheda Avvisi
- [ ] **Da fare dall'utente nel registro acquisti (pagina Acquisti → Registro acquisti)** — Commander Deck Set e Scene Box Set di Star Trek e acquisti passati di The Hobbit e Reality Fracture (`#HOB_OLD`, `#FRA_OLD`) registrati dall'utente il 2026-10-08. Resta:
  - Alle aperture di Star Trek (box in arrivo a novembre): segnare la data di apertura sull'acquisto (la previsione si calcola in quel momento) e usare il tag proposto dal pulsante accanto al campo (`#TRK_PB_AAAAMMGG`, `#TRK_CB_AAAAMMGG`) anche sulle inserzioni
- [ ] **Da fare subito dall'utente, senza codice**: alle prossime aperture usare il Tag `CODICE_TIPO_AAAAMMGG` (es. `TRK_PB_20261115`) al posto del tag fisso per espansione

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
- [x] ~~**Decidere quando uscire dal dry-run**~~ — deciso: il profilo gira in modalità reale da alcune notti, dopo le verifiche progressive rese possibili da «Applica le selezionate» nell'anteprima e da «Applica comunque» sulle carte bloccate dal guardrail
- [ ] **Affinare i percentili guardando l'anteprima**: partenza a 15% sul bulk, 20% fra 1 e 25 €, 40% sopra i 25 €. Dal 2026-09-02 l'anteprima si può restringere per fascia di prezzo ed espansione, quindi ogni percentile si prova sulle carte che governa. L'analisi di sensibilità su 11 carte reali mostra che le carte davvero sottoprezzo danno lo stesso risultato dal 20% al 60% — il segnale è robusto — mentre il percentile decide sulle altre. Da notare che sui mercati profondi il percentile è più aggressivo del vecchio ordinale (su Overgrown Tomb si passa dalla terza alla quinta posizione)
- [x] ~~**Grafici di andamento prezzi**~~ — fatto e verificato in produzione il 2026-09-05, nella pagina "Nuovo Prodotto" invece che in una scheda dedicata: nuovo endpoint `GET /api/cardtrader/blueprints/{id}/price-history`, grafico Chart.js con una linea per inserzione (condizione/lingua/foil possono differire). Insieme, aggiunti anche il link "Vedi su Card Trader" e il nome italiano/numero di raccolta sotto l'immagine, e una colonna CT nella griglia "Coda inserzioni"
- [x] ~~**Affiancare il riferimento di mercato al grafico storico**~~ — fatto e verificato in produzione il 2026-09-05, insieme al punto sotto: linea tratteggiata "Riferimento di mercato" nel grafico di "Nuovo Prodotto", da `PriceChangeLogs.ReferenceSellerPrice`. Il grafico è stato poi spostato nella colonna "Le mie inserzioni" (allargata a 340px) su richiesta, anche questo verificato. Riserva aperta: su carte con molte inserzioni diverse la colonna risulta un po' affollata — nessun intervento programmato, si valuta con l'uso
- [x] ~~**Allineare la scala di `PriceChangeLogs.ReferencePrice`**~~ — fatto il 2026-09-05: nuovo campo `ReferenceSellerPrice` (migration `AddReferenceSellerPriceToPriceChangeLogs`), che `PricingEngine.Evaluate` calcola dividendo `ReferencePrice` per il sovrapprezzo di Card Trader — esattamente come previsto qui ("basta una colonna"). Null sulle valutazioni scritte prima che il campo esistesse
- [ ] **Valutare se i ribassi vadano concessi affatto**: con il percentile al 40% restano 5 carte su 11 con proposta in ribasso. Il guardrail le limita al 25%, ma si può anche disattivare `CanDecrease` per fascia se l'obiettivo è solo cogliere i rialzi
- [x] ~~**Forzare il prezzo proposto sulle carte bloccate dal guardrail, dalla scheda Storico**~~ — fatto e verificato in produzione il 2026-09-05: nuovo parametro `bypassGuardrail` che percorre `PricingEngine.Evaluate` → `AutoPricingService.RunAsync` → `PricingRunCoordinator` → `POST /api/pricing/apply` (stesso endpoint dell'anteprima, con `BypassGuardrail: true` sul corpo). Nella scheda Storico la griglia dei calcoli mostra la casella di selezione solo sulle righe con esito `BlockedByGuardrail` — senza «seleziona tutto», un bypass va scelto carta per carta — e il pulsante "Applica comunque" rivaluta su dati freschi come «Applica le selezionate». L'utente ha confermato in produzione che forza correttamente il prezzo su una carta bloccata
- [x] ~~**Filtro per Origine nella scheda Esecuzioni**~~ — fatto e verificato in produzione il 2026-09-05, emerso durante la verifica del punto sopra: senza filtro, una giornata con molte "Nuova inserzione" faceva sparire la notturna della stessa notte oltre il limite di righe caricate. Menù a tendina per Origine (Notturna/Vendita/Manuale/Nuova inserzione) + limite salito da 20 a 50

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
- [ ] **Vendita su due canali, Card Trader + Cardmarket** — bloccata: l'API Cardmarket non accetta nuove richieste (verificato il 2026-10-07) e l'utente non ha credenziali precedenti. Anche con l'API, la parte delicata è scalare la disponibilità sull'altro canale a ogni vendita: la sincronizzazione offerta dal sito Card Trader ha già prodotto doppie vendite

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
