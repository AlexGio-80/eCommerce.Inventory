# eCommerce.Inventory - Richieste di Implementazione

> Feature progettata il 2026-10-15 con l'utente. Si appoggia sulla 004 (analisi acquisto prodotti
> sigillati): stessi dati Cardmarket e MTGJSON, stessa classifica giornaliera, stessi avvisi.

---

## 1. Descrizione

**Titolo feature:** `Magazzino da tenere`
**Priorità:** Media

La 004 risponde a due domande su un prodotto sigillato: conviene aprirlo, o conviene rivenderlo chiuso?
Manca una terza: **conviene comprarlo oggi e tenerlo**, perché fra uno o tre anni varrà di più, aperto o
chiuso? La feature segnala i prodotti con un prezzo d'acquisto interessante in questo senso, li manda
negli avvisi e tiene il conto di quelli comprati per tenerli: costo, valore di oggi, guadagno latente.

### Decisioni prese con l'utente (2026-10-15)

| Decisione | Scelta |
|---|---|
| Orizzonte | **1–3 anni**: si compra fra 6 e 24 mesi dall'uscita, quando il prezzo dei sigillati di solito tocca il minimo, e si rivende quando il prodotto esce di stampa |
| Segnalazione | Scheda "Da tenere" nelle Opportunità e **tipo di regola d'avviso nuovo**, dentro l'email di riepilogo |
| Magazzino | **Sì, nel registro acquisti**: segno "da tenere" sull'acquisto e vista con valore attuale e guadagno latente |
| Prezzo d'acquisto della resa | **Il trend, o l'offerta più bassa se è più alta del trend** (vedi Fase 0) |

---

## 2. Componenti Coinvolti

- [x] Backend (Infrastructure: `SealedProductAnalysisService`, `SealedOpportunityService`, `AlertService`, `ProductPurchaseService`)
- [x] Frontend (pagina Acquisti: schede Opportunità e Registro acquisti, editor delle regole d'avviso)
- [x] Database (migration nella Fase 1 e nella Fase 3)
- [ ] Integrazione esterna: nessuna nuova, si usano i dati già importati

---

## 3. Dati disponibili (verificati il 2026-10-15)

- **Storico Cardmarket dei sigillati:** parte dal 2026-10-07. Per dire se un prezzo è "basso per quel
  prodotto" servono almeno 2–3 mesi di storico, quindi questo segnale entra a regime da gennaio 2027.
- **Medie a 1, 7 e 30 giorni di Cardmarket:** il listino pubblico le dà solo per le singole, per i
  sigillati sono sempre vuote. Non si possono usare.
- **Valore atteso dell'apertura** (dalla 004): è il "pavimento" del prodotto. Se le carte contenute
  valgono quasi quanto il prezzo, il rischio di ribasso è limitato: anche se il sigillato non sale, aprendolo
  si recupera gran parte della spesa.
- **Data di uscita** del set principale, da `MtgjsonSets.ReleaseDate`.
- **Prova sui dati del 15/10:** fra le uscite di 6–48 mesi fa, 20 box, bundle e mazzi in "Tieni
  sigillato" hanno un contenuto che vale almeno il 75% del prezzo. Esempi: Draft Box di March of the
  Machine (84%), Collector Box di Final Fantasy (77%), parecchi mazzi Commander di 2–4 anni fa.

---

## 4. Fasi

### Fase 0 — Rese irreali da prodotti non comprabili *(pubblicata e verificata il 2026-10-15)*

Nella classifica restavano in "Apri" prodotti con rese del 100–500% che non si possono comprare a quel
prezzo:
- **Nessuna offerta su Cardmarket** (low vuoto): il trend viene da vendite vecchie. Esempi: i mazzi del
  Duel Deck Merfolk vs Goblins (+548%), i mazzi Salvat, il Game Night. Erano 20 in "Apri".
- **Offerta più bassa sopra il trend:** al trend non si trova niente (Salvat Elfos: trend 8,65 €,
  offerta più bassa 26 €). Erano 55 in "Apri", e per 30 la resa sul low diventa negativa.

Soluzione:
- decisione nuova **"Nessuna offerta"**, senza resa (`SealedProductAnalysisService.HasNoOffers`);
- la resa dell'apertura si calcola su `PurchasePrice` = trend, oppure il low se è più alto. Se il low è
  più basso resta il trend: il venditore più economico è spesso lontano o con poche vendite (decisione
  del 2026-10-07).

Il netto della rivendita chiusa resta sul trend: è a quel prezzo che si vende.

### Fase 1 — Punteggio "da tenere" e scheda nelle Opportunità *(pubblicata e verificata il 2026-10-15: 47 candidati)*

Ogni giorno, insieme alla classifica, si calcola per ogni prodotto un **punteggio da tenere** (0–100).
Calcolo in `HoldScoreCalculator`, soglie in `Purchasing:Hold:*`.

**Prodotti valutati**
- Tipi (`Category` di MTGJSON): `booster_box`, `bundle`, `box_set`, `deck`, `deck_box`, `subset`,
  `multiple_decks`. Esclusi buste singole, prodotti per draft e prerelease, kit, case.
- Esclusi i prodotti con decisione "Prezzo CM dubbio", "Nessuna offerta", "Dati incompleti" o "Non
  giocabili": senza un prezzo o un valore affidabili il punteggio non ha senso.
- Valutati sia "Apri" sia "Tieni sigillato", ma **candidati solo con resa dell'apertura negativa**
  (`MaxOpeningRoiPercent` = 0). Chi rende già aprendolo è un'occasione "compra e apri", già nella lista
  Apri e nei suoi avvisi. Da tenere sono i prodotti le cui carte coprono quasi tutto il prezzo ma che oggi
  non conviene ancora aprire. Senza questa regola, nella prova del 15/10, 405 candidati su 405 erano già
  in "Apri" con resa positiva; con la regola sono 104.
- **Secret Lair esclusi** (sottotipi `secret_lair*`), per scelta dell'utente del 15/10: li apre sempre,
  e li segue nella loro scheda e nella valutazione del drop. Inoltre il set SLD ha la data del 2019 per
  tutti i drop, quindi l'età non si potrebbe calcolare. Un prodotto senza data d'uscita ha l'età neutra
  (50 punti) e non è escluso.

**Componenti del punteggio**

| Componente | Peso | Come si calcola |
|---|---|---|
| Pavimento | 50% | Valore atteso netto dell'apertura / prezzo d'acquisto. 100 punti dal 100% in su, 0 punti al 60% o sotto, lineare in mezzo |
| Età dell'uscita | 30% | 0 punti sotto i 6 mesi (il prezzo di solito sta ancora scendendo). Pieno fra 12 e 30 mesi, a salire fra 6 e 12. A scendere fino a 0 a 60 mesi: chi è già uscito di stampa da anni ha di solito già fatto il suo rialzo |
| Prezzo rispetto al proprio storico | 20% | Quanto il trend di oggi è vicino al minimo dello storico del prodotto (100 = al minimo, 0 = al massimo). Con meno di 60 giorni di storico vale 50, neutro |

**Candidato da tenere:** punteggio ≥ 60 **e** pavimento ≥ 75% **e** resa dell'apertura negativa **e**
età fra 6 e 48 mesi (o non nota). Le soglie si tarano dopo le prime settimane d'uso.

**Prova su una copia del backup del 14/10, con il listino del 15/10:** 104 candidati, di cui 50 Secret
Lair, poi esclusi: ne restano circa 54, soprattutto mazzi Commander e box di 1–3 anni fa, con un
pavimento fra l'87% e il 100%. Esempi: Chocobo Bundle di Final Fantasy, box di Assassin's
Creed, Set Booster Box di Commander Masters, mazzi Commander di Duskmourn e Tarkir.

**Interfaccia:** nella scheda Opportunità, voce "Da tenere" nel filtro Decisione. In quella vista i
filtri sulla resa non si applicano. Colonne nuove: punteggio (in verde i candidati), pavimento %, età in
mesi, posizione nello storico. Il suggerimento sul punteggio spiega le tre parti.

**Dati:** campi nuovi su `SealedOpportunity`, scritti dal calcolo giornaliero insieme agli altri.

| Campo | Tipo C# | Note |
|-------|---------|------|
| `HoldScore` | `decimal?` | 0–100; null se il prodotto non è valutabile |
| `FloorPercent` | `decimal?` | Valore atteso netto / prezzo d'acquisto × 100 |
| `ReleaseAgeMonths` | `int?` | Mesi dall'uscita del set principale |
| `HistoryPosition` | `decimal?` | 0–100, null finché lo storico è più corto di 60 giorni |
| `IsHoldCandidate` | `bool` | Candidato secondo le soglie del giorno: lo legge la regola d'avviso della Fase 2 |

Migration: `AddHoldScore`.

### Fase 2 — Regola d'avviso "Da tenere"

Tipo nuovo `AlertRuleType.HoldCandidate = 4`: il prodotto **diventa** candidato da tenere con punteggio
almeno pari alla soglia. Come le altre regole:
- ambito per prodotto o per gruppo (set, tipo, sottotipo, uscite recenti);
- scatta quando la condizione diventa vera, non finché resta vera;
- entra nell'email di riepilogo.

Proposta di regola iniziale, da creare solo dopo l'ok dell'utente: "Da tenere, box e mazzi", soglia 70,
email sì. Il primo giorno il rumore sarà alto (tutti i candidati sono "nuovi"): valutare di crearla
spenta e accenderla il giorno dopo il primo calcolo.

### Fase 3 — Magazzino nel registro acquisti

- Campo nuovo su `ProductPurchase`: `HoldForResale` (`bool`, default `false`). Nel registro, una casella
  "Da tenere" nella maschera dell'acquisto. Migration `AddHoldForResale`.
- Vista **"Magazzino"** (sezione del Registro acquisti, o scheda a sé) con gli acquisti da tenere ancora
  sigillati:

| Colonna | Fonte |
|---|---|
| Prodotto, quantità, data d'acquisto, mesi di possesso | Registro |
| Costo totale | Quantità × prezzo unitario |
| Valore di oggi chiuso (netto) | Trend CM di oggi, al netto dei costi di vendita |
| Offerta più bassa CT | `SealedProduct.CtMinPrice` |
| Valore di oggi aperto (netto) | Valore atteso dell'apertura dalla classifica |
| Guadagno latente, € e % | Il migliore fra chiuso e aperto, meno il costo |
| Andamento | Trend di oggi rispetto al trend alla data d'acquisto, se nello storico |

Riga di totale: costo, valore e guadagno latente di tutto il magazzino.

Da valutare dopo l'uso, fuori da questa fase: un avviso quando un prodotto del magazzino supera un
guadagno latente scelto ("è il momento di vendere").

---

## 5. Note Tecniche

- Il punteggio vive nella classifica giornaliera (`SealedOpportunityService`): niente calcoli alla
  richiesta, e lo storico del punteggio resta per tarare le soglie.
- Le soglie (pesi, 60/75/6–48 mesi) vanno in configurazione (`Purchasing:Hold:*`) come gli altri
  parametri del valore atteso, per tararle senza ripubblicare.
- **Repository pubblico:** nessun acquisto reale dell'utente in questo documento, nei test o nelle
  migration.
