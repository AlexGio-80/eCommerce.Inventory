# eCommerce.Inventory - Richieste di Implementazione

> Template da compilare quando si vuole definire una nuova feature prima di iniziare a scriverla.
> Copiare questo file, rinominarlo con il numero progressivo e il titolo (es. `004-NuovaFeature.md`).

---

## 1. Descrizione

**Titolo feature:** `{TitoloFeature}`
**Priorità:** Alta / Media / Bassa

> [Descrivi in 2-3 frasi cosa deve fare la feature, quale problema risolve]

---

## 2. Componenti Coinvolti

- [ ] Backend (API / Infrastructure / Domain)
- [ ] Frontend (Angular)
- [ ] Database (nuova migration)
- [ ] Integrazione esterna (Card Trader API)

---

## 3. Modello Dati (se applicabile)

### Nuove entità / campi

| Campo | Tipo C# | Note |
|-------|---------|------|
| ... | ... | ... |

### Migration necessaria
- [ ] Sì — nome migration: `{YYYYMMDD}_{NomeMigration}`
- [ ] No

---

## 4. Endpoint API (se applicabile)

| Metodo | Path | Descrizione |
|--------|------|-------------|
| GET | `/api/...` | ... |
| POST | `/api/...` | ... |

---

## 5. Approccio Tecnico

> [Descrivere l'approccio: pattern da usare, file da creare/modificare, vincoli da rispettare]

### File da creare
- `src/eCommerce.Inventory.Domain/...`
- `src/eCommerce.Inventory.Infrastructure/...`
- `src/eCommerce.Inventory.Api/Controllers/...`
- `frontend/.../...`

### File da modificare
- ...

---

## 6. Criteri di Accettazione

- [ ] [Criterio 1]
- [ ] [Criterio 2]
- [ ] [Criterio 3]

---

## 7. Note Tecniche

> [Vincoli API Card Trader, side effect noti, dipendenze da altri componenti, workaround]
