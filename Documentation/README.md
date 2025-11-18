# eCommerce.Inventory - Documentation

Benvenuto nella documentazione di eCommerce.Inventory, un sistema di gestione inventario modulare per piattaforme di commercio elettronico specializzate in carte collezionabili (TCG).

---

## 📚 Documenti

### 1. [ARCHITECTURE.md](./ARCHITECTURE.md)
**Per**: Sviluppatori che devono capire la struttura del progetto

Contiene:
- Panoramica dell'architettura Clean Architecture
- Stack tecnologico completo
- Descrizione dei 4 strati (Domain, Application, Infrastructure, API)
- Flusso dei dati
- Schema del database
- Design patterns utilizzati
- Principi SOLID applicati
- Logging configuration
- Estensibilità per nuovi marketplace

**Quando leggerlo**: Quando entri nel progetto, prima di iniziare a modificare codice.

---

### 2. [IMPLEMENTATION.md](./IMPLEMENTATION.md)
**Per**: Sviluppatori che lavorano con il codice

Contiene:
- Implementazione dettagliata di ogni entità
- Interfacce e loro responsabilità
- Implementazioni di repository e servizi
- Configurazione di Program.cs
- Endpoint API disponibili
- Flussi principali (creazione item, sync, webhook)
- Logging details
- Strategie di testing
- Performance considerations
- Security implemented

**Quando leggerlo**: Quando devi modificare o debuggare codice specifico.

---

### 3. [SPECIFICATIONS.md](./SPECIFICATIONS.md)
**Per**: Chiunque sviluppi new features

Contiene:
- Principi SOLID (5 principi spiegati con esempi)
- Logging best practices
- Marketplace controller separation (come aggiungere eBay, Wallapop, etc.)
- Dependency injection requirements
- Entity Framework Core best practices
- API design patterns
- Async/await requirements
- Code organization standards
- Error handling patterns
- Configuration management
- Security guidelines
- **Checklist per nuovo feature**

**Quando leggerlo**: Prima di scrivere codice nuovo - **OBBLIGATORIO**.

---

### 4. [ROADMAP.md](./ROADMAP.md)
**Per**: Project manager, developers, stakeholder

Contiene:
- Status attuale (✅ completato, 🔨 in progress, ⏳ TODO)
- 8 fasi di implementazione con timeline dettagliata
- Phase 1: Database & Migrations
- Phase 2: Card Trader API Integration
- Phase 3: API Controller Enhancement
- Phase 4: Testing
- Phase 5: Advanced Features (Polly, Caching, Rate Limiting)
- Phase 6: Marketplace Expansion
- Phase 7: DevOps & Deployment
- Phase 8: Monitoring & Analytics
- Recommended development order (3 settimane)
- Success criteria per completamento

**Quando leggerlo**: Per capire cosa fare dopo, quando devi pianificare, per valutare progress.

---

## 🎯 Quick Start

### Per chi inizia

1. Leggi **ARCHITECTURE.md** (20 minuti)
   - Capisce come è organizzato il codice

2. Leggi **SPECIFICATIONS.md** (15 minuti)
   - Impara le regole di development

3. Leggi **IMPLEMENTATION.md** (30 minuti)
   - Capisce il codice che c'è

Tempo totale: ~65 minuti

### Per chi aggiunge una feature

1. Consulta il checklist in **SPECIFICATIONS.md**
2. Segui l'architectural pattern mostrato in **IMPLEMENTATION.md**
3. Aggiorna **ROADMAP.md** se necessario

---

## 📋 Checklist per Nuovo Developer

- [ ] Leggi ARCHITECTURE.md
- [ ] Leggi SPECIFICATIONS.md
- [ ] Leggi IMPLEMENTATION.md
- [ ] Scarica il progetto
- [ ] Esegui `dotnet build` (deve passare)
- [ ] Esegui `dotnet run` per testare
- [ ] Esplora il codice seguendo la struttura in ARCHITECTURE.md

---

## 🚀 Current Status

**Version**: 0.1 (Foundation & Architecture)

**Completato**:
- ✅ Architettura Clean Architecture
- ✅ Entità Domain complete
- ✅ DbContext EF Core
- ✅ Repository pattern
- ✅ API Controllers (Card Trader specific)
- ✅ Serilog logging
- ✅ Dependency injection
- ✅ API skeleton per Card Trader
- ✅ Background sync worker skeleton
- ✅ Documentazione completa

**Prossimi Passi**:
1. Database migrations (Phase 1)
2. Implementare API client (Phase 2)
3. Webhook processing (Phase 2)
4. Testing (Phase 4)

Vedi **ROADMAP.md** per timeline completa.

---

## 🏗️ Project Structure

```
eCommerce.Inventory/
├── eCommerce.Inventory.Domain/          ← Entità di business
├── eCommerce.Inventory.Application/     ← Interfacce
├── eCommerce.Inventory.Infrastructure/  ← Implementazioni
├── eCommerce.Inventory.Api/             ← Web API
└── Documentation/                       ← Questo folder!
    ├── ARCHITECTURE.md
    ├── IMPLEMENTATION.md
    ├── SPECIFICATIONS.md
    ├── ROADMAP.md
    └── README.md (questo file)
```

---

## 💡 Key Concepts

### Clean Architecture
Il progetto è organizzato in 4 strati indipendenti:
- **Domain**: Entità pure senza dipendenze esterne
- **Application**: Interfacce e contratti
- **Infrastructure**: Implementazioni (DB, API, services)
- **API**: Esposizione REST

Beneficio: Facile da testare, manutenere, e aggiungere nuovi marketplace.

### Marketplace Separation
Ogni marketplace ha i propri controller in `/api/{marketplace}/...`:
- `/api/cardtrader/inventory` - Card Trader inventory
- `/api/ebay/inventory` - eBay inventory (futuro)
- `/api/wallapop/inventory` - Wallapop inventory (futuro)

Beneficio: Zero impatto su altri marketplace quando ne aggiungi uno.

### Solid Principles
Ogni componente segue SOLID principles (vedi SPECIFICATIONS.md):
- SRP: Single Responsibility
- OCP: Open/Closed
- LSP: Liskov Substitution
- ISP: Interface Segregation
- DIP: Dependency Inversion

Beneficio: Codice manutenibile e estensibile.

---

## 🛠️ Useful Commands

```bash
# Build
dotnet build

# Run
dotnet run --project eCommerce.Inventory.Api

# Migrations (quando Phase 1 è pronto)
dotnet ef migrations add MigrationName --project eCommerce.Inventory.Infrastructure
dotnet ef database update

# Tests (quando Phase 4 è pronto)
dotnet test

# Package restore
dotnet restore
```

---

## 🤝 Contributing

Quando contribuisci con nuovo codice:

1. Leggi **SPECIFICATIONS.md** - Checklist
2. Segui il pattern in **IMPLEMENTATION.md**
3. Assicurati che il build passa: `dotnet build`
4. Aggiorna la documentazione se necessario

---

## ❓ FAQ

**D: Posso modificare l'architettura?**
R: No. SOLID principles e Clean Architecture sono fondamentali. Se trovi che non funziona per un caso, apri una discussion.

**D: Devo sempre usare Serilog per il logging?**
R: Sì. Console.WriteLine è vietato. Consulta SPECIFICATIONS.md.

**D: Posso aggiungere un nuovo marketplace?**
R: Sì! Segui la struttura in IMPLEMENTATION.md e crea `Controllers/NewMarketplace/`.

**D: Come gestisco i segreti (API token)?**
R: In development usa User Secrets. In production usa Azure Key Vault o simile. MAI hardcodare!

**D: Qual è il prossimo passo per il progetto?**
R: Phase 1: Database & Migrations. Vedi ROADMAP.md.

---

## 📞 Support

Se hai domande:

1. Consulta i documenti sopra
2. Guarda il codice - è ben commentato
3. Controlla ARCHITECTURE.md per pattern

---

## 📜 License

eCommerce.Inventory è sviluppato per usi commerciali.

---

**Last Updated**: 17 Novembre 2025
**Status**: Foundation & Architecture Complete ✅
**Next Review**: Dopo Phase 1 Completion
