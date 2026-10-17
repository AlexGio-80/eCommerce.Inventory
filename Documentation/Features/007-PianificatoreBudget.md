# eCommerce.Inventory - Richieste di Implementazione

> Feature progettata il 2026-10-17 con l'utente, che ha approvato le scelte della tabella "Decisioni".
> Unisce in una schermata il lavoro che oggi l'utente fa a mano fra la classifica, Card Trader, Cardmarket
> e il negozio Secret Lair.

---

## 1. Descrizione

**Titolo feature:** `Pianificatore del budget`
**Priorità:** alta (la schermata che l'utente userebbe di più per gli acquisti)
**Stato (2026-10-17):** in produzione e verificate dall'utente la Fase 0 (piano automatico), la Fase 1 (venditori Cardmarket incollati) e la vista "Per venditore", che è la principale; l'utente lo prova qualche giorno, poi si rivedono soglie e migliorie. Fase 2 facoltativa.

L'utente oggi parte da "questa settimana posso spendere X": guarda le proposte della classifica e degli
avvisi, le apre una per una su Card Trader, Cardmarket e sul negozio Wizards, poi compone i carrelli a mano
in base al budget e alle spedizioni. Il pianificatore fa lo stesso lavoro con i dati che il gestionale ha già
(valore atteso dell'apertura, punteggio "da tenere", storico dei prezzi, quota venduta per uscita, stime
Secret Lair) e propone **quali prodotti comprare, da chi e quanti, dentro il budget**. Il carrello lo fa
l'utente; su Card Trader resta il pulsante che lo riempie via API.

### Decisioni prese con l'utente (2026-10-17)

| Tema | Scelta | Alternativa scartata |
|------|--------|----------------------|
| Aprire e tenere | **Il budget lo divide l'utente** con un cursore (es. 70% aprire, 30% tenere): gli "apri" si scelgono per guadagno atteso in euro, i "tieni" per punteggio e pavimento | Classifica unica (i "tieni" finirebbero sempre in fondo: il loro guadagno si vede fra 1–3 anni); solo aperture |
| Copie dello stesso prodotto | **Limite dalla quota venduta dell'uscita**, al massimo 3 | Una copia sola; senza limite |
| Cardmarket senza venditori | **Al prezzo del listino**, con spedizione stimata e la nota "venditore da cercare"; con le pagine del venditore incollate diventa un carrello vero. Nella prima stesura il prezzo era il più basso ("From"); dal 17/10 è il trend, o il più basso se è più alto, come la classifica (il "From" è in qualsiasi lingua) | Solo con le pagine incollate |
| Spedizioni | **Stima fissa per negozio, modificabile** (Cardmarket: quella letta dalla pagina, se c'è) | Ignorarle |
| Cardmarket, venditori | **Pagine del venditore copiate e incollate dall'utente** (Fase 1) | Lettura automatica: Cardmarket la blocca con la verifica anti-bot di Cloudflare (provato il 17/10) e le sue condizioni la vietano; navigazione dal Chrome dell'utente: rischio sull'account Cardmarket, che è anche quello da venditore |

---

## 2. Componenti Coinvolti

- [x] Backend (Infrastructure: servizio del pianificatore, lettura delle pagine Cardmarket incollate; API: endpoint del piano)
- [x] Frontend (scheda "Budget" nella pagina Acquisti)
- [x] Database: Fase 1 (offerte dei venditori Cardmarket incollate); Fase 0 nessuna migration
- [ ] Integrazioni nuove: nessuna (Card Trader marketplace e carrello, listino Cardmarket e negozio Wizards ci sono già)

---

## 3. Cosa c'è già (verificato nel codice il 2026-10-17)

| Pezzo | Dove | Cosa dà al pianificatore |
|-------|------|--------------------------|
| Classifica giornaliera | `SealedOpportunityService`, tabella `SealedOpportunities` | Per ogni prodotto: valore atteso netto dell'apertura, resa, decisione, punteggio "da tenere", pavimento, età, posizione nello storico |
| Piano d'acquisto Card Trader | `PurchasePlanService` | Offerte vere per venditore (solo inglese, non in vacanza), raggruppate per venditore e carrello CT Zero; "Metti nel carrello" via API. **Non conosce budget né spedizioni**, lavora su prodotti scelti a mano, al massimo 40 |
| Listino Cardmarket | `CardmarketLatestPrices`, `CardmarketProducts` | Trend e prezzo più basso di ogni prodotto, ogni mattina. Nessun venditore. I nomi dei prodotti sono **identici** a quelli delle pagine dei venditori (verificato sui mazzi Final Fantasy e Aetherdrift) |
| Storico prezzi Cardmarket | `CardmarketPriceSnapshots` | Andamento del trend nei giorni (dal 07/10) |
| Negozio Secret Lair | `SecretLairShopMonitorService`, `SecretLairDropValuationService` | Prodotti in vendita da Wizards con prezzo, valore netto stimato, margine e verdetto (Compra / Al limite / Lascia) |
| Quota venduta | `BulkSellThroughService`, `PriceRealizationService` | Quanto delle carte caricate di un'uscita si è venduto: dice se un'altra apertura della stessa uscita si smaltisce |
| Magazzino | Registro acquisti, segno "Da tenere" | Copie già tenute di un prodotto, da contare nel limite delle copie |

---

## 4. Fasi

### Fase 0 — Pianificatore con Card Trader, Secret Lair e Cardmarket al prezzo più basso *(pubblicata e verificata il 2026-10-17)*

**Schermata** (scheda "Budget" nella pagina Acquisti):

- In alto: **budget** in euro, **cursore aprire/tenere** (predefinito 70/30), casella "Secret Lair anche Al
  limite" (spenta: solo "Compra"), pulsante **"Calcola il piano"**. Il calcolo legge le offerte Card Trader
  di 30–40 prodotti: 1–2 minuti per il limite delle 20 richieste al minuto, con l'avanzamento a schermo.
- **Riepilogo:** speso, guadagno atteso delle aperture, valore "pavimento" dei tenuti, spedizioni, budget
  rimasto (e perché: "nessun altro prodotto conveniente sotto i 40 €").
- **Carrelli**, uno per negozio e venditore: Card Trader Zero (una spedizione per tutto), ogni venditore Card
  Trader, Cardmarket ("venditore da cercare" finché non c'è la Fase 1), negozio Wizards. Per ogni riga:
  prodotto, Apri/Tieni/Secret Lair, quantità, prezzo, valore atteso o punteggio, guadagno, avvisi sul tempo.
  Per ogni carrello: totale con spedizione e guadagno. Pulsanti: "Metti nel carrello Card Trader" (quello di
  oggi), link al prodotto su Cardmarket e al drop su Wizards.
- Si può togliere una riga: il piano si ricalcola con le offerte già lette, senza rileggere Card Trader.

**Candidati** (dalla classifica di oggi):

- *Aprire:* decisione "Apri" con resa almeno `Purchasing:Budget:MinOpenRoiPercent` (proposta 10%), esclusi
  "Prezzo CM dubbio", "Dati incompleti", "Non giocabili", "Nessuna offerta". Più i Secret Lair in vendita su
  Wizards con verdetto "Compra" (e "Al limite" se richiesto): si vendono carta per carta come un'apertura.
- *Tenere:* candidati "da tenere" (`IsHoldCandidate`), ordinati per punteggio.
- I migliori 40 in tutto (il limite del piano Card Trader di oggi), divisi fra i due gruppi in proporzione al
  cursore.

**Scelta dentro il budget** (due parti di budget, una per gruppo; ciò che avanza a un gruppo passa all'altro):

- Ogni offerta (prodotto × negozio × venditore) ha un costo e un valore: per aprire, **guadagno = valore
  atteso netto − prezzo**; per tenere, **punteggio**, con il pavimento come caso peggiore.
- Si aggiunge un'offerta alla volta, la migliore per guadagno rispetto al costo, dove il costo comprende la
  **spedizione solo se il venditore non è ancora nel piano**: il secondo prodotto dallo stesso venditore non
  la paga. Ci si ferma quando nessuna offerta rimasta ha guadagno positivo o sta nel budget.
- Prezzi Card Trader in vetrina come li paga l'utente; Cardmarket al "From" del listino; Wizards al prezzo del
  negozio.

**Copie dello stesso prodotto:** al massimo 3. Con la quota venduta dell'uscita sotto il 50% al massimo 2,
sotto il 25% 1. Uscite non misurabili (meno di 200 copie fra vendute e in vendita, o mai caricate): 2, perché
le proprie inserzioni non si fanno concorrenza (corretto in implementazione: la prima stesura diceva 1).
Quota venduta = copie vendute negli ordini pagati / (vendute + in vendita) delle carte dell'uscita. I Secret
Lair del negozio Wizards: una copia. Le copie già in magazzino "Da tenere" contano nel limite dei tenuti.
Soglie in `Purchasing:Budget:*`.

**Spedizioni stimate** (`Purchasing:Budget:Shipping:*`, modificabili nella schermata): Card Trader per
venditore, Card Trader Zero una volta sola, Cardmarket per venditore, Wizards. Nel repository solo valori
generici; quelli che l'utente ricava dai suoi ordini vanno in `appsettings.Production.json` o si cambiano
dalla schermata.

**Avvisi sul tempo**, accanto alla riga (non cambiano la scelta, aiutano a decidere):
- "In calo": trend Cardmarket sceso di oltre il 5% negli ultimi 7 giorni: forse conviene aspettare;
- "Caro rispetto allo storico": posizione nello storico sotto 30 (trend vicino al massimo visto);
- "Uscita recente": meno di 6 mesi, di solito il prezzo scende ancora;
- per i Secret Lair, i giorni rimasti in vendita su Wizards, se il negozio li dice.

**Come è fatto** (2026-10-17): `BudgetPlanner` (scelta pura, testata), `BudgetPlanService` (candidati,
offerte Card Trader dalla cache di 10 minuti o lette, al massimo 6 per prodotto, Cardmarket al "From" con una
copia, Secret Lair Wizards dalla valutazione), `BudgetPlanRunner` (singleton, un calcolo alla volta, piani in
memoria 6 ore), scheda `budget-plan-tab`. Impostazioni `Purchasing:Budget:MinOpenRoiPercent` (10),
`MaxOpenRoiPercent` (300, come gli avvisi), `MaxCandidates` (40), `MaxCopies` (3), `Shipping:CardTrader` (7),
`Shipping:CardTraderZero` (8), `Shipping:Cardmarket` (7), `Shipping:Wizards` (10): valori generici. Il cursore,
il budget e le spedizioni scelte si ricordano nel browser.

**Endpoint:** `POST /api/purchasing/budget-plan` (budget, quota aprire, opzioni) avvia il calcolo in
background e restituisce un id; `GET /api/purchasing/budget-plan/{id}` dà avanzamento e risultato;
`POST /api/purchasing/budget-plan/{id}/recompute` ricalcola senza rileggere Card Trader (righe tolte,
spedizioni cambiate); `GET /api/purchasing/budget-plan/settings` dà le spedizioni predefinite. Il piano resta
in memoria qualche ora: le offerte invecchiano.

**Prima prova (2026-10-17, 300 €, 70/30, spedizioni dell'utente):** 28 + 12 prodotti letti, 289 offerte, speso
299,20 €. Le aperture sono andate tutte a 5 drop Secret Lair del negozio Wizards (guadagno atteso 437 €). Fra i
tenuti c'era lo Spider-Man Welcome Deck da Cardmarket a 9,99 € più 12 € di spedizione (trend 15 €) e da CT
Zero a 20,27 € più 15 €: corretto con un tetto per i tenuti, **prezzo più spedizione da pagare al massimo il
trend Cardmarket più il 10%** (`Purchasing:Budget:MaxHoldPremiumPercent`). Una spedizione già pagata dallo
stesso venditore non conta, quindi i tenuti si raccolgono dai venditori già nel piano.

**Seconda prova (17/10, 500 €, tutto tenere):** solo carrelli Cardmarket, nessun Card Trader. Il prezzo più
basso del listino Cardmarket è l'offerta più economica in qualsiasi lingua, mentre su Card Trader si prendono
solo offerte in inglese: Cardmarket vinceva sempre (Bloomburrow Play Booster Box a 80 € contro 172 € di trend).
Corretto: **Cardmarket senza venditore al trend, o al prezzo più basso se è più alto**, come la classifica (decisione
del 15/10 sulla resa). I prezzi Cardmarket veri per venditore arrivano con la Fase 1.

**Terza prova (17/10, 500 €, tutto tenere):** Card Trader in gara (Chocobo Bundle da un venditore portoghese a
249,99 € contro 262,86 € di trend), ma due soli prodotti per 434 €. Scelta dell'utente: **più varietà**, nessun
prodotto da tenere oltre il 40% della parte "tenere" del budget (`Purchasing:Budget:MaxHoldProductSharePercent`,
prezzo di tutte le copie, spedizione esclusa).

**Esito (2026-10-17):** con 500 € tutto tenere, 4 prodotti in un unico carrello Card Trader Zero (una spedizione,
tutti sotto o intorno al trend Cardmarket), una box da Cardmarket e un Secret Lair Wizards con l'avanzo: speso
499,12 €. L'utente: "i carrelli sono quelli che farei".

**Limiti noti:** "Metti nel carrello Card Trader" aggiunge una copia per offerta (come il piano di oggi): la
seconda copia dalla stessa offerta va aggiunta sul sito. Del prezzo più basso Cardmarket si presume una sola
copia. Il 17/10 in cima alle aperture ci sono soprattutto drop Secret Lair sul mercato secondario (rese del
100–200% sul modello): da guardare con l'utente alla prima prova.

**Verifica:** con un budget di 300 € il piano propone carrelli che l'utente farebbe anche a mano; i prezzi
Card Trader coincidono con il sito; lo stesso venditore compare con più prodotti quando conviene; nessun
prodotto oltre il limite di copie; budget rimasto spiegato.

### Fase 1 — Pagine dei venditori Cardmarket incollate *(pubblicata e verificata il 2026-10-17)*

- Nella scheda "Budget", riquadro **"Incolla le pagine di un venditore Cardmarket"**: l'utente apre le pagine
  delle offerte del venditore (es. `.../Users/{venditore}/Offers/Preconstructed-Decks`, `.../Offers/Sets`,
  pagina 2 compresa), con il filtro **Lingua: Inglese**, seleziona tutto, copia e incolla. Il gestionale non
  apre mai Cardmarket da solo.
- Il testo si legge riga per riga: nome del prodotto, prezzo, spedizione, quantità disponibile, nota
  ("DAMAGED!" esclude l'offerta). Il nome si abbina al catalogo del listino (`CardmarketProducts`) e da lì al
  prodotto sigillato e alla sua riga della classifica. Le righe non abbinate si mostrano, per controllo.
- Le offerte si salvano con venditore e data (tabella nuova, migration) e restano finché il venditore non si toglie
  (all'inizio valevano 3 giorni; cambiato il 17/10 su richiesta dell'utente); il piano le usa
  come carrelli veri di quel venditore, con la sua spedizione, al posto del "venditore da cercare".
- Dopo l'incolla, subito: "di questo venditore ti conviene anche …", cioè il piano ristretto a quel venditore.

**Da provare all'inizio della fase:** come arriva il testo copiato. La lingua sulla pagina è una bandierina
(un'immagine), che nel testo copiato non c'è: per questo il filtro Lingua: Inglese prima di copiare. Se il
testo copiato non basta (righe spezzate, prezzi senza separatori), si valuta di salvare la pagina (Ctrl+S) e
caricare il file.

**Testo copiato, provato il 17/10** su una pagina vera (18 mazzi Commander in inglese): per ogni offerta nome,
commento, prezzo ("37,98 €") e quantità su righe separate; il venditore e la categoria nel percorso in alto; il
filtro lingua si vede come "English (18)". **La spedizione non c'è nel testo**: si usa quella impostata per
Cardmarket. Nel listino importato i nomi hanno le virgolette raddoppiate (`""Counter Blitz""`, difetto dell'import
del CSV): l'abbinamento normalizza virgolette e spazi.

**Come è fatto:** tabella `CardmarketSellerOffers` (migration `AddCardmarketSellerOffers`), `CardmarketSellerPageParser`
(testo semplice, con tabulazioni o con link Markdown), `CardmarketSellerOfferService` (abbinamento ai soli
sigillati del listino, sostituzione delle offerte dello stesso venditore per i prodotti incollati, valutazione con la
classifica di oggi), endpoint `POST /api/purchasing/cardmarket-sellers/paste`, `GET /api/purchasing/cardmarket-sellers`,
`DELETE /api/purchasing/cardmarket-sellers/{venditore}`. Nel pianificatore ogni offerta valida (3 giorni,
`Purchasing:Budget:CardmarketOfferDays`, non danneggiata) è una riga del venditore, con una spedizione per
venditore; un prodotto incollato entra anche se non è fra i 40 migliori, se al prezzo del venditore conviene
aprirlo (resa fra 10% e 300%) o è un candidato da tenere. Il riquadro "Venditori Cardmarket" della scheda Budget
mostra subito la valutazione di ogni offerta e le righe non abbinate.

**Prova del 17/10:** la pagina Preconstructed Decks di un venditore: 18 offerte abbinate su 18, venditore e categoria
riconosciuti, prezzi e quantità giusti, l'offerta "DAMAGED!" esclusa; 4 offerte convenienti da aprire (resa 10–40%
al prezzo del venditore). Nel piano però non comparivano: anche con 3.000 € il budget finiva prima, sui 38 Secret
Lair del negozio Wizards (circa 2.000 €) e su quelli del mercato secondario, che rendono il 100–200% secondo il
modello. Aggiunte le caselle **"Compro su: Card Trader / Cardmarket / Wizards"**: un negozio spento esce dal piano,
che si ricalcola subito sulle offerte già lette (richiesta `ExcludedStores`).
Anche con Card Trader e Wizards spenti i mazzi del venditore restavano fuori: ogni prodotto Cardmarket "da cercare"
paga la sua spedizione (26 carrelli, 312 €) e il budget finiva sui Secret Lair. Aggiunto il pezzo previsto e
mancante, **"Da questo venditore ti conviene"**: sotto la tabella delle offerte incollate, i prodotti consigliati del
solo venditore con una spedizione (quella impostata per Cardmarket), totale e guadagno atteso, sopra la tabella.

**Esito (2026-10-17):** sulla pagina di un venditore, "ti conviene: 4 prodotti, 165,35 € + 12 € di spedizione =
177,35 €, guadagno atteso 24,27 €", come i conti fatti a mano; righe convenienti in evidenza (sfondo, barra e
valutazione in grassetto, su richiesta dell'utente).

**Verifica:** le pagine di un venditore della mattina del 17/10 (28 mazzi Commander, 5 "Sets") danno le stesse
righe che vede l'utente, con prezzi e quantità giusti; il piano propone più prodotti dello stesso venditore nello
stesso carrello quando convengono.

### Ritocchi dopo l'uso (pubblicati e verificati il 2026-10-17)

- **Secret Lair a vendita chiusa:** il pianificatore proponeva il Dandân Deck, che sul sito è "No longer
  available". Il catalogo di Wizards tiene anche i drop finiti (sezione Chaos Vault) con una scorta di comodo:
  212 dei 247 prodotti che risultavano disponibili avevano la data di fine vendita passata. Nuovo stato **"Vendita
  chiusa"** (`SecretLairShopMonitorService.StatusOf`), escluso dal piano e dal filtro "In negozio" della scheda
  Secret Lair.
- **Campo "Venditore Cardmarket"** con i pulsanti che aprono le sue pagine delle offerte in inglese e per nome
  (`.../Users/{venditore}/Offers/{categoria}?idLanguages=1&sortBy=name_asc`: Mazzi, Set, Box di buste, Buste, tutte).
- **Venditori incollati riapribili:** toccando il nome si rivedono offerte, valutazione e carrello consigliato dal
  database (`GET /api/purchasing/cardmarket-sellers/{venditore}`), e la scheda riapre da sola l'ultimo guardato:
  cambiando scheda non si reincolla più.
- **Virgolette raddoppiate nel listino:** vengono dal catalogo JSON di Cardmarket; tolte all'import
  (`CardmarketPriceImportService.CleanName`). Nessuna migration: l'import del mattino riscrive tutti i nomi.

### Per venditore (pubblicata e verificata il 2026-10-17)

L'utente, dopo le prove: "io ragiono per venditore cercando di ridurre le spese di spedizione, che in caso di acquisti da
più venditori spesso si mangiano il guadagno". Il piano automatico ragiona per prodotto (il miglior insieme di
acquisti, i venditori ne sono la conseguenza); questa vista ragiona per venditore, ed è il fulcro del blocco Acquisti.
Regole scelte dall'utente:

| Regola | Valore predefinito (modificabile nella schermata) |
|---|---|
| **Traino:** almeno un prodotto da aprire con resa, al prezzo del venditore, di almeno | 50% (`Purchasing:Budget:Sellers:AnchorRoiPercent`) |
| **Aggiunte:** le altre aperture dello stesso venditore con resa di almeno | 33% (`Sellers:AddOnRoiPercent`) |
| **Magazzino:** i suoi prodotti da tenere, solo se c'è già un'apertura, a non oltre il trend + | 10% (`MaxHoldPremiumPercent`) |
| **Vale la spedizione** se il guadagno delle aperture, tolta la spedizione, è almeno questa quota della spesa per aprirle | 20% (`Sellers:MinNetPercent`) |
| Copie | una per prodotto, all'offerta più economica del venditore |

- **Fonti:** Card Trader con tutte le offerte delle espansioni dei prodotti che interessano (da aprire con resa al trend
  almeno del 5%, o candidati da tenere: 405 prodotti in 110 espansioni il 17/10, una chiamata per espansione, circa 5
  minuti); ogni venditore Card Trader col suo carrello, più **Card Trader Zero** come carrello unico fra più venditori
  con una spedizione (lo stesso venditore CT Zero può comparire anche da solo). Cardmarket: i venditori incollati.
- **Ordine:** venditori per guadagno netto; si segnano "nel budget" quelli che ci stanno presi in quell'ordine.
- **Wizards a parte**, con il suo importo (predefinito 500 €, l'abitudine dell'utente ogni due settimane): i drop
  "Compra" acquistabili, il margine migliore per primo.
- La lettura resta in memoria qualche ora (`SellerPlanRunner`); soglie, budget, negozi e spedizioni si cambiano e si
  ricalcola con "Aggiorna", senza rileggere. Codice: `SellerBasketBuilder` (regole, testato), `SellerPlanService`,
  endpoint `api/purchasing/budget-plan/sellers`. Nella scheda Budget la vista "Per venditore" è la prima; il piano
  automatico resta come "Piano automatico".

**Prima prova (17/10, 500 €):** 429 prodotti, 110 espansioni lette in circa 7 minuti, 2.758 offerte, 60 venditori
convenienti, 3 nel budget. Corretti subito:
- **Carrelli dentro il budget:** il carrello Card Trader Zero prendeva tutto (6.783 € con 500 di budget). Ora ogni
  carrello si riempie col traino che rende di più fra quelli che ci stanno, poi con le aperture che rendono di più,
  poi con i tenuti dal punteggio più alto, finché il totale con la spedizione sta nel budget; un venditore il cui
  traino da solo non ci sta non compare.
- **"Aggiorna" rilegge i venditori Cardmarket incollati** dal database: un venditore incollato dopo la ricerca entra
  subito, senza rileggere Card Trader.
- **Tieni:** al posto della resa dell'apertura (numeri come "-3%"), il prezzo rispetto al trend ("3% sotto").

**Seconda prova (17/10, 900 €):** un solo carrello nel budget, Card Trader Zero (894 €, guadagno netto 622 €); l'utente
trova sensati anche i venditori successivi e vuole usare Cardmarket così: dai prodotti interessanti cerca i venditori,
incolla le loro pagine e confronta. Correzioni chieste:
- **Tutti i prodotti dei venditori incollati:** la vista considerava solo i prodotti che al trend rendono almeno il 5%,
  e solo con decisione "Apri". Ora i venditori Cardmarket si valutano su tutto quello che hanno, al loro prezzo, anche i
  prodotti "Tieni sigillato" (al trend conviene rivenderli chiusi, ma comprati sotto il trend aprirli può rendere bene:
  esempio dell'utente, un box del Signore degli Anelli a buon prezzo). Esclusi dati incompleti, prezzi dubbi, non
  giocabili, senza offerte (`SellerBasketBuilder.IsOpenable`). Vale anche per la valutazione subito dopo l'incolla e
  per il piano automatico.
- **I venditori incollati non scadono più** (`CardmarketOfferDays` predefinito 0): restano finché l'utente non li
  toglie. La data dell'incolla si vede sul nome e sulla scheda del venditore, in arancione dopo 7 giorni.
- **La pagina 1 reincollata sostituisce la categoria** ("Page 1 of N" letto dal testo): i prodotti venduti spariscono;
  le pagine successive si aggiungono.

**Terza prova (17/10, un venditore con 514 offerte):** prezzi sbagliati (un Fallout Deck Set a 13,69 € con resa del
1968%). Da utente collegato Cardmarket mostra sotto il prezzo la **spedizione dell'articolo** ("248,69 €", "13,69 €",
"1"), e la spedizione veniva letta come prezzo. Corretto: il parser legge prezzo e spedizione; la spedizione si salva con
l'offerta (migration `AddCardmarketSellerOfferShipping`) e il carrello di quel venditore usa la più alta fra gli articoli
scelti, al posto della stima fissa per Cardmarket. Rese oltre il 300% nella tabella dell'incolla: "Prezzo da controllare".

**Quarta prova (17/10, secondo venditore):** la pagina di un altro venditore è finita salvata sotto il primo, perché il
campo del venditore (rimasto sul primo) prevaleva sul nome della pagina; e da utente collegato il menu della quantità
("1") accanto a ogni offerta veniva preso per il nome del prodotto dopo (2 abbinate su 8). Corretti: vale il venditore
della pagina, il campo serve solo ad aprire le pagine; le righe fatte solo di un numero si ignorano. Le 2 offerte
salvate sotto il venditore sbagliato tolte dal database.

**Quinta prova (17/10, 900 €):** dati giusti (un venditore con 508 offerte, un altro con 47 su 47). Le due proposte migliori: Card
Trader Zero (894 €, guadagno netto 625 €) e il venditore Cardmarket da 508 offerte (898 €, 484 €). Ritocchi chiesti dall'utente:
- **"Da X ti conviene" con le regole della schermata** (traino, altri, netto, budget): prima usava le aperture sopra il
  10% e diceva "7 prodotti" per un venditore che nella lista non c'era; ora dice anche perché un venditore resta fuori
  (es. "manca un traino: l'apertura che rende di più rende il 49%").
- **"anche in: …"** sulle righe dei prodotti presenti in più ordini della lista.
- **"Scelgo questo"** su ogni ordine e, in cima, il totale della propria combinazione: spesa rispetto al budget,
  spedizioni, guadagno netto, prodotti che si comprerebbero due volte.
- **Ultima lettura salvata** (tabella `SellerPlanSnapshots`, migration `AddSellerPlanSnapshots`): a fine ricerca la
  lettura Card Trader si salva al posto della precedente; dopo un riavvio o una pubblicazione la scheda la riapre,
  ricalcolata con i venditori Cardmarket incollati di adesso, e mostra la data delle offerte (in arancione dopo 7
  giorni). "Cerca i venditori" solo quando si vogliono offerte fresche.

**Esito (2026-10-17):** verificato dall'utente ("direi ci sia tutto"): riquadro del venditore con le regole e il motivo
dell'esclusione, "anche in", "Scelgo questo" con il totale e gli avvisi di budget e doppioni, lettura salvata (1,4 MB).

### Fase 2 — Dopo l'acquisto (facoltativa, da rivalutare dopo l'uso)

- "Segna come comprato" su un carrello: crea le righe del registro acquisti con prodotto, quantità, prezzo
  pagato, negozio e il segno "Da tenere" per i tenuti. Oggi il registro si compila a mano.
- Budget della settimana che si scala con gli acquisti segnati.

---

## 5. Criteri di Accettazione

- [ ] Il piano non supera mai il budget, spedizioni comprese
- [ ] Aperture e tenuti rispettano la divisione del cursore (salvo il budget che avanza a un gruppo)
- [ ] Nessun prodotto oltre il limite di copie, contando il magazzino per i tenuti
- [ ] Ogni riga dice perché c'è: guadagno atteso o punteggio, e gli avvisi sul tempo
- [ ] Card Trader: "Metti nel carrello" funziona sul piano come oggi
- [ ] (Fase 1) Le pagine incollate danno le righe che si vedono su Cardmarket; le righe non abbinate si mostrano

---

## 6. Note Tecniche

- Il limite di 20 richieste al minuto è condiviso: il calcolo passa dal limitatore come il piano di oggi, senza
  la precedenza interattiva della maschera (che resta veloce durante il calcolo). Le offerte si possono
  prendere dalla cache di 10 minuti (`MarketplaceOfferCache`) quando ci sono già.
- La scelta dentro il budget è un "problema dello zaino" con le spedizioni: con 40 prodotti e qualche
  centinaio di offerte basta la scelta un'offerta alla volta descritta sopra, ricalcolata in millisecondi
  quando l'utente toglie una riga.
- Repository pubblico: le spedizioni e i venditori degli ordini dell'utente non vanno in documentazione, test
  o migration; i venditori incollati restano solo a database.
- Riuso: il piano Card Trader di oggi (scheda Opportunità) resta com'è; il pianificatore ne usa la lettura
  delle offerte e il carrello.
