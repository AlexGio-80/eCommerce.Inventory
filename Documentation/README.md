# eCommerce.Inventory

> Sistema di gestione inventario per carte collezionabili (TCG), integrato con la piattaforma Card Trader. Permette di sincronizzare l'inventario, gestire ordini, creare inserzioni e analizzare la redditività.

---

## Prerequisiti

- [.NET 8 SDK](https://dotnet.microsoft.com/download)
- SQL Server 2019+ (o SQL Server Express) — istanza `DEV-ALEX\MSSQLSERVER01` in sviluppo
- Node.js 20+ e Angular CLI (`npm install -g @angular/cli`)
- PowerShell 5.1+ (per gli script di deploy)

---

## Configurazione

### 1. Clona il repository

```bash
git clone {repo-url}
cd eCommerce.Inventory
```

### 2. Configura `appsettings.json`

In `src/eCommerce.Inventory.Api/appsettings.json` verificare e adattare:

- `ConnectionStrings:DefaultConnection` — connection string SQL Server
- `CardTraderSettings:BearerToken` — token API Card Trader
- `JwtSettings:SecretKey` — chiave JWT (min 32 caratteri)
- `BackupSettings:BackupPath` e `BackupDestinationPath` — percorsi backup

> In produzione i segreti vanno in `appsettings.Production.json` sul server (non committare).

### 3. Applica le migration

```bash
dotnet ef database update --project src/eCommerce.Inventory.Infrastructure --startup-project src/eCommerce.Inventory.Api
```

---

## Avvio

### Sviluppo (backend)

```bash
cd src/eCommerce.Inventory.Api
dotnet run
```

Swagger disponibile su `http://localhost:5152/swagger`

### Sviluppo (frontend)

```bash
cd frontend/ecommerce-inventory-ui
npm install
ng serve
```

Frontend su `http://localhost:4200`

### Produzione (deploy completo)

```powershell
# Eseguire come Administrator
.\publish.ps1
```

Lo script compila backend e frontend, aggiorna il Windows Service e l'IIS site.

Vedi [DEPLOY.md](DEPLOY.md) per la procedura dettagliata e la prima installazione.

---

## Test

```bash
dotnet test
```

---

## Struttura

```
src/
├── eCommerce.Inventory.Domain/          ← Entità, interfacce repository, enum
├── eCommerce.Inventory.Application/     ← DTOs, interfacce service, CQRS commands/queries
├── eCommerce.Inventory.Infrastructure/  ← EF Core, repository, Card Trader API client, worker
└── eCommerce.Inventory.Api/             ← Controller, middleware, Program.cs
tests/
└── eCommerce.Inventory.Tests/
frontend/
└── ecommerce-inventory-ui/              ← Angular SPA
Documentation/
├── ARCHITECTURE.md  ← Struttura, pattern, modello dati, decisioni tecniche
├── CONTEXT.md       ← Snapshot stato corrente per riprendere velocemente
├── ROADMAP.md       ← Cosa c'è da fare, cosa è stato fatto
├── CHANGELOG.md     ← Log sessioni di lavoro
├── DEPLOY.md        ← Procedura di deploy e prima installazione
├── SPECIFICATIONS.md← Standard e principi obbligatori di sviluppo
└── Features/        ← Documentazione feature specifiche
```

Vedi [ARCHITECTURE.md](ARCHITECTURE.md) per dettagli architetturali.

---

## Logs

I log vengono scritti in `Publish/api/logs/` con rolling giornaliero.
Livello: `Debug` in sviluppo, `Information` in produzione (configurabile in `appsettings.json`).
