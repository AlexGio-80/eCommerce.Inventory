# eCommerce.Inventory - Richieste di Implementazione

> Feature progettata il 2026-10-16 con l'utente, che ha approvato le scelte della tabella "Decisioni".
> Porta sul telefono tre lavori che oggi si fanno solo al PC di casa.

---

## 1. Descrizione

**Titolo feature:** `App per smartphone`
**Priorità:** ultima grande feature in programma
**Stato (2026-10-16):** progetto approvato; Fasi 0, 1 e 2 pubblicate e verificate (accesso con la guida in `DEPLOY.md`, "Accesso dal telefono"); Fase 3 (pending list dal telefono) pubblicata e verificata il 2026-10-17: tutte le fasi sono in produzione.

L'utente vuole usare il gestionale dal telefono, in quest'ordine di importanza:

1. **Fotografare una carta e farla valutare subito**: riconoscimento della carta e stima della
   condizione con l'IA. Il pezzo era stato abbozzato nella maschera "Nuovo Prodotto" e fermato
   perché fare le foto, passarle sul PC e poi valutarle era troppo lento.
2. **Evadere la pending list** ("Nuovo Prodotto", `PendingListings`) dal telefono.
3. **Valutare al volo una carta fuori casa**: prezzi Card Trader e Cardmarket, prezzo delle regole
   dell'autopricer.

### Decisioni prese con l'utente (2026-10-16)

| Tema | Scelta | Alternativa scartata (per ora) |
|------|--------|-------------|
| Tipo di app | **PWA sul frontend Angular di oggi**: si apre dal browser del telefono e si installa sulla schermata Home | App ibrida con Capacitor (stesso Angular, pacchetto da installare); app nativa .NET MAUI (interfaccia da riscrivere) |
| Accesso da fuori casa | **Tailscale** (VPN privata, nessuna porta aperta sul router) con HTTPS di Tailscale Serve | Cloudflare Tunnel con Cloudflare Access |
| Riconoscimento della carta | **Claude (API Anthropic) con visione**: legge nome, sigla del set, numero di raccolta e lingua dalla foto; la carta si trova poi con la ricerca dei blueprint che c'è già | Ximilar (riconoscimento carte TCG, a pagamento) |
| Stima della condizione | **Claude, come suggerimento** da confermare a mano | Ximilar Grading (abbonamento a pagamento, già predisposto nel codice) |
| Modello Claude | **Claude Opus 5.5**, il più capace della serie corrente | Claude Sonnet 5.5 o Claude Haiku 5.5, più economici: si decide dopo averli provati sulle stesse foto (vedi §6) |
| Telefono | **Android**: PWA installabile da Chrome; se un giorno servisse Capacitor, basta un APK installato a mano, senza store né account sviluppatore | — |

---

## 2. Componenti Coinvolti

- [x] Backend (API: endpoint di riconoscimento, valutazione rapida; Infrastructure: client Anthropic, prezzo Cardmarket di tutte le singole)
- [x] Frontend (Angular: PWA, indirizzo dell'API letto all'avvio, pagine per il telefono)
- [ ] Database: nessuna migration (l'ultimo prezzo Cardmarket di tutte le singole c'era già)
- [x] Integrazione esterna: **API Anthropic** (nuova, solo in uscita), **Tailscale** (nuovo, sul PC e sul telefono)
- [x] Configurazione sul PC di casa: Tailscale come servizio, Tailscale Serve, CORS

---

## 3. Cosa c'è già (verificato nel codice il 2026-10-16)

### Grading con IA, abbozzato a dicembre 2025

- **Backend:** `GradingController` (`POST /api/grading/analyze` e `/analyze-multi`, foto fronte e
  retro con la strategia "voto peggiore"), interfaccia `IGradingService`, `GradingResultDto` (voto
  complessivo, centratura, angoli, bordi, superficie, confidenza, codice condizione NM/SP/MP/PL/PO).
- **`XimilarGradingService` è un mock:** senza `Ximilar:ApiKey` restituisce voti casuali, e anche con
  la chiave la lettura della risposta (`ParseXimilarResponse`) è un `TODO` che restituisce valori
  fissi. La traduzione voto → condizione (≥ 8 NM, ≥ 6 SP, ≥ 4 MP, ≥ 2 PL) si può riusare.
- **Database:** `PendingListing` ha già i campi del grading (`GradingScore`, `GradingConditionCode`,
  `GradingCentering`, `GradingCorners`, `GradingEdges`, `GradingSurface`, `GradingConfidence`,
  `GradingImagesCount`), salvati da `PendingListingsController` quando la maschera li manda.
- **Frontend:** pagina "AI Grading" (`features/grading`) con la webcam del PC (`getUserMedia`,
  camera posteriore se c'è); nella maschera "Nuovo Prodotto" una sezione "AI Grading" con caricamento
  di file (`accept="image/*" multiple`). Su un telefono quel campo apre già la fotocamera: il
  collo di bottiglia non era il codice, era il passaggio delle foto dal telefono al PC.
- **Manca del tutto il riconoscimento della carta:** il grading presuppone che la carta sia già
  stata scelta a mano nella maschera.

### Quello che serve alla valutazione al volo, già pronto

- Ricerca carte `GET /api/cardtrader/blueprints/search` (nome, nome italiano, numero di raccolta).
- Statistiche del mercato Card Trader `GET /api/cardtrader/inventory/marketplace-stats/{blueprintId}`,
  con la cache delle offerte di 10 minuti e la precedenza nel limite di 20 richieste al minuto.
- Prezzo delle regole `GET /api/pricing/suggest/{blueprintId}` (stesso motore della notturna).
- Storico prezzi `GET /api/cardtrader/blueprints/{id}/price-history`, inserzioni proprie
  `GET /api/pending-listings/by-blueprint/{blueprintId}`.
- Pending list completa: elenco, creazione, modifica, cancellazione, invio a Card Trader
  (`POST /api/pending-listings/sync`).

### Ostacoli trovati

1. **Il frontend chiama l'API su `http://localhost:5152`** (`environment.prod.ts`, fissato alla
   compilazione). Dal telefono "localhost" è il telefono stesso: oggi la pagina si aprirebbe e ogni
   chiamata fallirebbe. L'indirizzo dell'API va ricavato dalla pagina in cui gira il frontend.
2. **La fotocamera dal browser e la PWA richiedono HTTPS.** `http://inventory.local` va bene sul PC,
   ma dal telefono il browser nega la fotocamera e non installa l'app. Serve un certificato valido:
   Tailscale Serve lo dà già (indirizzo `*.ts.net`).
3. **Prezzi Cardmarket delle singole solo per le espansioni degli ultimi 12 mesi**
   (`CardmarketImport:SinglesTrackingMonths`): per una carta vecchia trovata in giro il prezzo CM
   oggi non c'è. *Corretto in Fase 1: l'ultimo prezzo di tutti i prodotti c'era già, in
   `CardmarketLatestPrices`.*
4. **CORS** accetta solo `localhost:4200` e `inventory.local`: va aggiunto l'indirizzo Tailscale.
5. **Il JWT dura 7 giorni e non è revocabile.** Su un telefono che si può perdere conta di più che
   sul PC (vedi §7).
6. **"Nuovo Prodotto" è una pagina da desktop** (1.000 righe di componente, griglie AG Grid, tre
   colonne): rimpicciolita su un telefono non si usa. Servono pagine apposite, piccole, che
   riusano servizi ed endpoint.

---

## 4. Opzioni a confronto

### Tipo di app

| | PWA (Angular di oggi) | Capacitor (Angular in un'app) | .NET MAUI |
|---|---|---|---|
| Riuso del codice | Totale: stessi servizi, stesso login, stessa pubblicazione | Quasi totale: si impacchetta la stessa build | Nessuno lato interfaccia: tutto da riscrivere in XAML/C# |
| Fotocamera | `getUserMedia` o campo file con fotocamera; sufficiente per foto di carte | Plugin nativo: messa a fuoco e flash migliori, foto a risoluzione piena | Nativa |
| Installazione | "Aggiungi a Home" dal browser, nessuno store | Android: APK da installare a mano. iPhone: serve l'account sviluppatore Apple (99 $/anno) o una reinstallazione ogni 7 giorni | Come Capacitor |
| Aggiornamenti | Automatici a ogni `publish.ps1` | Il contenuto web si può aggiornare, ma l'app va ricompilata a ogni cambio nativo | Ricompilare e reinstallare |
| Lavoro | Basso | Medio (progetto Android/iOS, firma, strumenti) | Alto |
| Funziona fuori casa | Sì, con Tailscale | Sì, con Tailscale | Sì, con Tailscale |

**Raccomandazione: PWA.** Copre i tre bisogni con il codice che c'è; la fotocamera del browser basta per
leggere una carta (non serve una scansione a risoluzione di laboratorio). Capacitor resta una strada
aperta *dopo*: si appoggia alla stessa build, quindi il lavoro della PWA non va perso. MAUI non conviene:
un secondo frontend da mantenere per un solo utente.

### Accesso da fuori casa (nessuna porta aperta sul router)

| | Tailscale | Cloudflare Tunnel + Access | VPN sul router (WireGuard/OpenVPN) |
|---|---|---|---|
| Porte aperte sul router | **Nessuna** (connessioni in uscita) | **Nessuna** (tunnel in uscita) | Una porta UDP: **esclusa** per la preferenza dell'utente |
| Chi può raggiungere il gestionale | Solo i dispositivi della propria rete Tailscale | Chiunque su internet arrivi alla pagina di login di Cloudflare Access | Solo chi ha la chiave |
| HTTPS | Certificato `*.ts.net` automatico con Tailscale Serve | Certificato Cloudflare, serve un dominio su Cloudflare | Da gestire |
| Chi vede il traffico | Nessuno: cifrato da telefono a PC (WireGuard) | Cloudflare decifra e ricifra (vede token e dati) | Nessuno |
| Sul telefono | App Tailscale accesa | Niente, basta il browser | App VPN |
| Costo | Piano personale gratuito | Gratuito per pochi utenti | Gratuito |

**Raccomandazione: Tailscale con Tailscale Serve.** L'API resta invisibile da internet, l'unico
prerequisito è l'app Tailscale sul telefono, e Serve dà l'HTTPS che serve a fotocamera e PWA.
**Tailscale Funnel no:** pubblicherebbe il servizio su internet, cioè esattamente l'esposizione che si
vuole evitare. Cloudflare resta l'alternativa se un giorno servisse un accesso senza app (per esempio
per il webhook Card Trader, oggi in stand-by), ma è un'esposizione pubblica da valutare a parte.

Configurazione proposta sul PC (nessuna modifica a Kestrel né a IIS):
- Tailscale installato come servizio, in modalità "unattended" (resta attivo senza utente collegato).
- `https://<pc>.<tailnet>.ts.net` → IIS (`http://localhost:80`, il frontend).
- `https://<pc>.<tailnet>.ts.net:8443` → API (`http://127.0.0.1:5152`), compreso l'hub SignalR.
- Regole d'accesso (ACL) di Tailscale: il telefono raggiunge solo le porte 443 e 8443 del PC.
- Il frontend ricava l'API dall'indirizzo della pagina: su `inventory.local` resta `localhost:5152`,
  su `*.ts.net` diventa la stessa macchina sulla porta 8443. Il PC di casa continua a funzionare come oggi.

### Riconoscimento della carta e stima della condizione

| | Claude con visione (API Anthropic) | Ximilar |
|---|---|---|
| Riconoscimento | Legge il testo stampato: nome (anche in italiano), sigla del set, numero di raccolta, lingua, segni di foil. Il nostro database fa il resto con la ricerca dei blueprint | Riconoscimento per immagine contro il proprio catalogo TCG |
| Stima della condizione | Indicativa: vede pieghe, graffi evidenti, angoli rovinati; non misura la centratura al decimo | Specializzato nel grading (centratura, angoli, bordi, superficie) |
| Integrazione | Chiamata singola dal backend .NET (SDK ufficiale C#), risposta in JSON strutturato | Già abbozzata (`XimilarGradingService`), da completare il parsing |
| Costo | A consumo, centesimi per carta (§6) | Abbonamento mensile a pagamento (CONTEXT, ROADMAP) |

**Raccomandazione:** Claude per il riconoscimento (è il pezzo che manca e quello che fa risparmiare
più tempo) e come primo stimatore della condizione, **sempre come proposta**: la condizione su Card
Trader la decide l'utente, perché un "NM" sbagliato si paga con un reclamo. Ximilar resta un'opzione
da provare solo se le stime di Claude si rivelano troppo approssimative sulle carte di valore; il codice
è già predisposto dietro `IGradingService`, quindi si può tenere uno o l'altro senza toccare il frontend.

---

## 5. Fasi

Ogni fase si pubblica e si verifica da sola, come per la 004 e la 005.

### Fase 0 — Accesso dal telefono, fuori casa *(pubblicata e verificata il 2026-10-16)*

- Tailscale sul PC (servizio) e sul telefono; Tailscale Serve per frontend e API; ACL. *Lavoro di
  configurazione, fatto dall'utente con una guida passo passo in `DEPLOY.md`.*
- Frontend: indirizzo dell'API ricavato dalla pagina (al posto di `environment.prod.ts` fisso),
  anche per SignalR.
- Backend: origine `*.ts.net` in CORS (da configurazione, non nel codice).
- Pagina di login usabile su schermo piccolo.

**Verifica:** dal telefono in 4G (Wi-Fi di casa spento) si apre `https://<pc>.<tailnet>.ts.net`, si fa
login, si vede la dashboard. Sul PC `http://inventory.local` funziona come prima. Con l'app Tailscale
spenta, dal telefono l'indirizzo non risponde.

### Fase 1 — PWA e "Valuta carta" (bisogni 1 e 3) *(pubblicata e verificata il 2026-10-16)*

Schermate approvate dall'utente il 2026-10-16, con tre scelte: dall'indirizzo Tailscale si entra
direttamente nella sezione per il telefono (link "Versione completa" per le pagine di sempre); le foto
non si salvano; al massimo 200 riconoscimenti al giorno.

- **PWA:** `manifest.webmanifest`, icone in `public/icons`, `sw.js` che non mette in cache niente (né file
  né dati: i prezzi sono sempre quelli di adesso, e a ogni pubblicazione il telefono vede la versione
  nuova). Il service worker si registra solo da un indirizzo `*.ts.net`.
- **Sezione `/m`** (`features/mobile`), barra in basso con **Scansiona** e **Cerca** (la **Coda** arriva con
  la Fase 3). L'indirizzo vuoto porta a `/m` dal telefono e alla dashboard dal PC (`isRemoteHost`).
- **Scansiona:** il pulsante apre la fotocamera di Android (`capture="environment"`), non un riquadro
  nella pagina: messa a fuoco e flash contano per leggere il numero di raccolta. Il telefono riduce la
  foto a 1.400 px sul lato lungo (JPEG) prima dell'invio. `POST /api/scan/recognize`: Claude
  (`ClaudeCardImageReader`, risposta JSON vincolata a uno schema) legge nome inglese e stampato, sigla del
  set, numero di raccolta, lingua, foil; `CardScanService` cerca fra le carte Magic quelle del set letto e
  quelle con lo stesso nome (inglese o italiano) e ne propone al massimo 5. Una carta del set entra solo se
  ha anche il nome o il numero giusto; la stampa con set e numero giusti è "esatta" e, se è la prima ed è
  l'unica, si va dritti alla scheda.
- **Scheda di valutazione** (`GET /api/scan/card/{blueprintId}?condition&language&isFoil`), con condizione,
  lingua e foil modificabili:
  - **prezzo delle regole** (`ListingPriceSuggestionService`, lo stesso della maschera);
  - Card Trader: offerta più bassa, più bassa CT Zero e numero di offerte della stessa condizione, lingua e
    foil, escluse le mie, firmate, alterate, gradate, in vacanza e sopra 1.000 € (prezzi in vetrina). Le
    offerte passano dalla cache di 10 minuti, con la precedenza nel limite di Card Trader;
  - Cardmarket: trend e low (foil se la carta è foil) dall'ultimo listino;
  - le mie inserzioni della carta, di qualsiasi condizione.
- **Cerca:** la ricerca della maschera "Nuovo Prodotto" (nome, nome italiano, numero), solo carte Magic.
- **Prezzo Cardmarket di tutte le singole: c'era già.** `CardmarketLatestPrices` tiene l'ultimo prezzo di
  ogni prodotto del listino (128.220 righe il 16/10), e il 98% delle carte Magic ha l'abbinamento con
  Cardmarket (121.775 su 124.045). Nessuna migration.
- Configurazione: `Anthropic:ApiKey` (solo in `appsettings.Production.json`), `Scan:Model` (predefinito
  `claude-opus-5-5`), `Scan:DailyLimit` (predefinito 200, contatore in memoria che si azzera anche al
  riavvio). Senza chiave il riconoscimento risponde che non è configurato; Cerca e la scheda funzionano.

**Verifica:** fotografare 20 carte miste (nuove e vecchie, italiane e inglesi, foil, frame speciali):
quante sono riconosciute al primo colpo e in quanti secondi dalla foto alla scheda. Obiettivo proposto:
almeno 17 su 20 al primo candidato, sotto i 10 secondi in 4G. Prezzo CM presente anche su una carta
di 10 anni fa.

**Esito (2026-10-16):** obiettivo raggiunto. Riconosciute carte nuove e vecchie, italiane e inglesi, e
tutte le Secret Lair provate. Quarta Edizione (senza simbolo né numero): carta giusta nell'elenco, stampa
scelta a mano, come previsto. Le extended art finivano sulla versione normale perché Card Trader le tiene
nelle espansioni "Collectors" (`cafr`, `cfra`): corretto lo stesso giorno, verificato dall'utente. Le art
card non si riconoscono, scelta accettata. Costo misurato: circa 2.700 token in ingresso e 100 in uscita,
circa 1,3 centesimi a carta con Opus 5.5; circa 6 s fra PC e Claude.

### Fase 2 — Stima della condizione (completamento del bisogno 1) *(pubblicata e verificata il 2026-10-16)*

Schermate e proposte approvate dall'utente il 2026-10-16.

- Nella scheda, **"Stima condizione"**: foto del fronte (obbligatoria) e del retro (facoltativa), ridotte
  sul telefono a 1.568 px sul lato lungo (il massimo che Claude usa davvero), in una sola chiamata a
  `POST /api/grading/analyze-multi`.
- `ClaudeGradingService` (nuova implementazione di `IGradingService`, effort medio, JSON vincolato a uno
  schema): condizione Card Trader (NM, SP, MP, PL, PO), sicurezza, voti 1-10 per centratura, angoli,
  bordi, superficie, e una motivazione in italiano per ciascuno. **Nel dubbio sceglie la condizione
  peggiore**; con bustina, riflessi o foto mossa lo dice e abbassa la sicurezza.
- **"Usa questa condizione"** imposta la condizione della scheda e ricalcola i prezzi: senza quel tocco non
  cambia niente.
- Il grading finto è tolto: `Grading:Provider` sceglie fra Claude (predefinito) e Ximilar, e Ximilar senza
  chiave risponde con un errore invece che con voti casuali (la lettura della risposta Ximilar resta da
  scrivere, se un giorno servisse). Anche la pagina "AI Grading" e la sezione "AI Grading" della maschera
  "Nuovo Prodotto" usano ora la stima vera; nella maschera la condizione proposta va nel campo, che si
  controlla prima di inviare (comportamento di prima).
- Dopo le prime prove dell'utente (16/10): **Near Mint solo con il retro fotografato e sicurezza alta**,
  altrimenti Slightly Played con la nota "Potrebbe essere Near Mint"; fino a **due foto "dettaglio"** (sotto
  la luce, controluce, primo piano di un difetto); istruzioni su sbeccature dei bordi neri, bordi dentellati,
  retro ingiallito; consiglio di riempire la foto con la carta; effort alto. La stima resta un primo filtro:
  alcuni difetti si vedono solo con la carta in mano.
- Quando la regola abbassa un Near Mint di Claude, accanto a "Usa questa condizione" (SP) compare **"Usa
  Near Mint"**: sulle foto Claude risponde quasi sempre con sicurezza media, e senza il pulsante NM sarebbe
  raggiungibile solo dal menu.
- Ogni stima conta come un riconoscimento nel limite `Scan:DailyLimit`. Il log scrive per ogni stima la
  proposta, la condizione detta da Claude, la sicurezza e le foto usate. Client Anthropic condiviso
  (`AnthropicClientProvider`). Le foto non si salvano.
- I voti si salveranno sui campi `Grading*` di `PendingListing` con la Fase 3, aggiungendo alla coda dal
  telefono.

**Verifica:** 20 carte di cui l'utente conosce la condizione (anche alcune SP e MP): quante stime
coincidono e, soprattutto, quante volte la stima è *migliore* della realtà (l'errore che costa). Se
quell'errore è frequente, si valuta Ximilar su quelle stesse foto.

### Fase 3 — Pending list dal telefono (bisogno 2) *(pubblicata e verificata il 2026-10-17)*

Schermate approvate dall'utente il 2026-10-17, con due scelte: le motivazioni della stima si salvano
(migration `AddPendingListingGradingNotes`, colonna `GradingNotes`), e dopo l'aggiunta si resta sulla
scheda (per aggiungere la stessa carta in un'altra condizione).

- **"Aggiungi alla coda"** nella scheda, sotto il prezzo delle regole: condizione, lingua e foil sono quelli
  della scheda; quantità con − e +; prezzo di vendita già quello delle regole, che segue i filtri finché non
  lo si cambia a mano; Tag con l'elenco dei Tag del registro acquisti (i più recenti prima) e l'ultimo usato
  ricordato dal telefono (`localStorage`); prezzo d'acquisto dal costo per carta del registro, con la regola
  della maschera (mai su un prezzo scritto a mano, non se il Tag ha più costi). `SuggestedPrice` si manda
  solo se il prezzo è quello delle regole per i filtri inviati: allora la pubblicazione non accoda il riprezzo.
- La **stima di Claude** fatta sulla carta va con l'inserzione: condizione proposta, voti, sicurezza, numero di
  foto e motivazioni (`gradingNotes()` in `grading.service.ts`, usata anche dalla maschera del PC). Resta
  anche dopo "Usa questa condizione", e si azzera dopo l'aggiunta: riguarda la copia appena messa in coda.
- **Pagina Coda** (`/m/queue`, terza voce della barra con il numero delle inserzioni in attesa): carte, numero
  di inserzioni e totale in euro; righe con "Modifica" per gli aggiornamenti di inserzioni già su Card Trader
  ed errori dell'ultimo invio in rosso; tocco sulla riga per cambiare quantità, prezzi, condizione, lingua e
  Tag (cambiando condizione o lingua il prezzo delle regole salvato non vale più e si toglie); "Elimina" toglie
  la riga subito e la cancella solo se non si tocca "Annulla" entro 4 secondi (nel frattempo l'invio è
  bloccato); **"Invia a Card Trader"** chiede conferma e usa `POST /api/pending-listings/sync`.

- Dalla scheda di valutazione, "Aggiungi alla coda": quantità, condizione, lingua, foil, prezzo (già
  quello delle regole, come nella maschera), prezzo d'acquisto e Tag (anche dal registro acquisti,
  come nella maschera).
- Pagina **Coda**: elenco delle `PendingListings` non inviate, modifica e cancellazione con un tocco,
  totale in euro, pulsante **"Invia a Card Trader"** (`POST /api/pending-listings/sync`, lo stesso del PC,
  con il riprezzo dopo la pubblicazione già ottimizzato il 16/10).
- La coda è la stessa del PC: si può iniziare in giro e finire alla scrivania, o il contrario.

**Verifica:** 10 carte caricate dal telefono finiscono su Card Trader con condizione, lingua, prezzo e
Tag giusti; nella maschera del PC la coda è la stessa; nel log, "pubblicati al prezzo delle regole" per
quelle lasciate al prezzo proposto.

**Esito (2026-10-17):** 4 inserzioni (5 carte) dal telefono su Card Trader con condizione, lingua, prezzo e Tag
giusti, e la copia locale creata subito; la stessa coda nella maschera del PC; modifica della quantità,
"Elimina" e "Annulla" come previsto. Nel log "3 blueprint pubblicati al prezzo delle regole" e un solo riprezzo,
per la carta a un prezzo diverso. La stima salvata con le motivazioni si è vista sulla prima prova (proposta SP,
scelta NM).

### Scorrimento del set nella scheda *(pubblicato e verificato il 2026-10-17)*

Richiesta dell'utente del 2026-10-17, dopo la Fase 3: caricare una serie di carte dello stesso set (un set
nuovo, per esempio) senza fotografarle una per una, come con i pulsanti ‹ › della maschera "Nuovo Prodotto".
Scelte approvate: pulsanti, swipe e "Vai al numero".

- In cima alla scheda **‹ #123 · 45 di 281 ›**; swipe orizzontale sulla parte alta (a sinistra la successiva);
  toccando il numero, **"Vai al numero"** ("54", "054" e "54/281" valgono uguale; con più stampe dello stesso
  numero si va alla prima).
- L'elenco del set arriva una volta per set (`GET /api/scan/set/{expansionId}`, carte senza numero escluse,
  ordine della maschera: i numeri contano come numeri, "12a" dopo "12"), quindi lo scorrimento è immediato.
- Passando di carta **restano** condizione, lingua, foil, Tag e prezzo d'acquisto; **ripartono** quantità (1),
  prezzo (quello delle regole della carta nuova) e stima. I prezzi si leggono 400 ms dopo l'ultimo tocco:
  scorrendo veloce si legge il mercato solo della carta su cui ci si ferma (limite di 20 richieste al minuto).
  Una risposta arrivata tardi per la carta di prima viene scartata. Lo scorrimento sostituisce la pagina nella
  cronologia: "indietro" torna a Scansiona o Cerca.

**Esito (2026-10-17):** verificato dall'utente su Modern Horizons 3 Collectors (272 carte, #320–521): ‹ ›, swipe,
tocchi rapidi, "Vai al numero" con un numero esistente e uno assente; elenco del set letto una sola volta.

### Fuori da queste fasi, da rivalutare dopo l'uso

- **Stima che impara dalle correzioni** (idea dell'utente del 16/10). Con la Fase 3 ogni inserzione caricata dal
  telefono avrà sia la condizione proposta (`GradingConditionCode`, con le motivazioni) sia quella scelta
  davvero (`Condition`): le differenze nette sono già un archivio di errori, senza lavoro in più. Da lì due
  strade, senza addestrare un modello: una revisione periodica (come quella degli avvisi) che propone
  ritocchi alle istruzioni della stima, da approvare; oppure le ultime correzioni nette, come brevi "lezioni"
  scritte, aggiunte in automatico alle istruzioni. Le valutazioni che solo una persona può dare (tolleranza
  sulle carte vecchie e di poco valore) restano fuori: sono regole di vendita, non di stima.

- **Capacitor**, se la fotocamera del browser si rivela scomoda (messa a fuoco, flash).
- **Scansione a raffica** (più carte di fila senza tornare alla scheda) per i lotti misti; per le carte dello stesso set c'è lo scorrimento del 17/10.
- **Ximilar**, se la stima della condizione di Claude non basta.
- **Lavoro senza rete** (coda salvata sul telefono e inviata al rientro): complica molto, e i prezzi
  senza rete sarebbero comunque vecchi.

---

## 6. Costi

| Voce | Costo |
|------|-------|
| Tailscale | Piano personale gratuito |
| PWA | Nessuno (nessuno store, stesso server) |
| Capacitor, se un giorno servisse | Nessuno su Android (APK installato a mano) |
| Ximilar, se un giorno servisse | Abbonamento mensile, listino da verificare al momento |
| API Anthropic | A consumo, sotto |

**Stima per riconoscimento** (prezzi Anthropic aggiornati al 2026-10-06; una foto ridotta a circa
1.000 × 1.400 px vale circa 1.900 token in ingresso, la risposta qualche centinaio di token):

| Modello | Prezzo per milione di token (ingresso / uscita) | Per carta, circa | 500 carte al mese |
|---|---|---|---|
| Claude Opus 5.5 (proposto) | 4 $ / 20 $ | 2 centesimi | 10 $ |
| Claude Sonnet 5.5 | 2 $ / 10 $ | 1 centesimo | 5 $ |
| Claude Haiku 5.5 | 0,10 $ / 0,50 $ | meno di 0,1 centesimi | meno di 0,50 $ |

La stima della condizione con due foto costa circa il doppio di un riconoscimento. Sono stime: il costo
vero si legge dal consumo restituito da ogni chiamata, che si scrive nel log. La scelta del modello è
dell'utente: la proposta è partire da Opus 5.5 e provare sulle stesse 20 foto della verifica della
Fase 1 anche Sonnet e Haiku, poi tenere il più economico che riconosce altrettanto bene.

---

## 7. Sicurezza

- **Nessuna porta aperta sul router.** Il gestionale è raggiungibile solo dai dispositivi della rete
  Tailscale dell'utente, e con le ACL solo sulle due porte necessarie.
- **Il token Card Trader e la chiave Anthropic restano sul PC**, nella configurazione di produzione non
  versionata. Il telefono parla solo con la nostra API, mai direttamente con Card Trader o Anthropic.
- **Login invariato** (utente `admin`, ruolo `Admin` su ogni endpoint). Tailscale è un secondo
  livello, non un sostituto.
- **Telefono perso:** si toglie il dispositivo dalla rete Tailscale dal pannello web e l'accesso
  finisce subito, anche se il JWT è ancora valido. Proposta aggiuntiva, da decidere: durata del token
  più breve per il telefono (per esempio 24 ore), oppure revoca di tutti i token cambiando una "versione"
  sull'utente. Non è indispensabile con Tailscale.
- **Dati di produzione:** le pagine del telefono usano gli stessi endpoint del PC, quindi nessuna
  scrittura nuova a database oltre a quelle che la maschera fa già (Fase 3) e alla tabella dei prezzi
  CM (Fase 1). Il database di sviluppo è quello di produzione: le prove della Fase 3 con carte vere si
  fanno con inserzioni che l'utente vuole davvero pubblicare, o su una copia del backup.
- **Foto:** inviate ad Anthropic solo per la risposta, **non conservate** sul PC salvo scelta diversa
  dell'utente. Sono foto di carte, senza dati personali; attenzione solo a non inquadrare altro.
- **Costi fuori controllo:** limite giornaliero di chiamate all'IA in configurazione, oltre al tetto di
  spesa impostabile sulla console Anthropic.
- **Repository pubblico:** nessun indirizzo Tailscale reale, chiave o foto dell'utente in
  documentazione, test o migration.

---

## 8. Rischi

| Rischio | Effetto | Mitigazione |
|---|---|---|
| Riconoscimento sbagliato fra stampe della stessa carta (ristampe, frame alternativi, promo) | Prezzo della stampa sbagliata | Il numero di raccolta e la sigla del set decidono; candidati con immagine; scelta finale dell'utente |
| Stima della condizione ottimista | Reclamo o rimborso su Card Trader | Solo proposta, mai applicata da sola; verifica della Fase 2 centrata su questo errore |
| PC di casa spento o rete di casa giù | Il telefono non funziona | Nessuna mitigazione nel progetto (vale anche oggi per il PC); il servizio è già ad avvio automatico |
| Limite di 20 richieste al minuto di Card Trader | Schede lente durante la notturna o un riprezzo | Precedenza alle richieste interattive e cache delle offerte, già in produzione dal 16/10 |
| Fotocamera del browser scomoda su alcuni telefoni | Foto sfocate, riconoscimento peggiore | Campo file con fotocamera di sistema come ripiego; Capacitor in seguito |
| Tailscale cambia piani o condizioni | Accesso da fuori da rifare | Nessun legame nel codice: l'indirizzo dell'API si ricava dalla pagina, si passa a un'altra soluzione cambiando solo la configurazione |
| Una pagina nuova rompe la maschera del PC | Caricamenti fermi | Pagine del telefono separate; la maschera "Nuovo Prodotto" non si tocca, si riusano solo servizi ed endpoint |

---

## 9. Note Tecniche

- Il client Anthropic va nell'Infrastructure come gli altri client esterni (`AddHttpClient`, o l'SDK
  ufficiale C#), con risposta in JSON strutturato (`output_config.format`) e gestione del caso
  `refusal`. Il riconoscimento è una sola chiamata, non un agente.
- Il riconoscimento non decide la carta: restituisce i campi letti, e la corrispondenza la fa
  `BlueprintRepository.SearchByNameAsync` (nome, nome italiano, numero di raccolta) più un filtro sulla
  sigla del set. Così una risposta dell'IA non può mai inventare un blueprint.
- L'indirizzo dell'API ricavato dalla pagina sostituisce `environment.apiUrl` in un punto solo (un
  servizio di configurazione letto da tutti i servizi Angular), non in ogni servizio.
- Il service worker non mette in cache le chiamate `/api`: niente prezzi vecchi mostrati come attuali.
- Nessun cambiamento a Kestrel (resta su `127.0.0.1:5152`) né al wiring di produzione in `Program.cs`.
