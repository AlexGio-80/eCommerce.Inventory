# Changelog - 2025-11-27

## Phase 3.12: Authentication & Security - COMPLETATO ✅

### Panoramica
Implementazione completa di un sistema di autenticazione JWT Bearer Token per proteggere l'applicazione, con gestione utenti, login sicuro, e protezione delle rotte.

### Modifiche Backend

#### 1. Entità e Database
- **Nuova Entità**: `User` (Username, PasswordHash, Role)
- **Migration**: `AddUserAuthentication` - Aggiunta tabella Users
- **Seed Data**: Utente admin di default (`admin` / `admin123`)
  - Fix: Controllo indipendente per username "admin" (non bloccato da Games.Any())

#### 2. Autenticazione JWT
- **AuthService**: Servizio per registrazione, login, hashing password (BCrypt), generazione token JWT
- **AuthController**: Endpoint `/api/auth/login` e `/api/auth/register`
- **Program.cs**: Configurazione JWT Bearer Authentication
  - Issuer, Audience, SecretKey validation
  - Token expiration: 24 ore
- **CORS**: Policy aggiornata per supportare credenziali e origini specifiche
  - `WithOrigins("http://localhost:4200", "http://127.0.0.1:4200", "http://inventory.local")`
  - `AllowCredentials()`

#### 3. Sicurezza
- **Password Hashing**: BCrypt.Net-Next (work factor 10)
- **JWT Tokens**: System.IdentityModel.Tokens.Jwt
- **Protected Endpoints**: Tutti gli endpoint API richiedono Bearer Token valido

### Modifiche Frontend

#### 1. Servizi di Autenticazione
- **AuthService**: Login/logout, gestione token (localStorage), gestione sessione
  - `login(username, password)`: Autenticazione e salvataggio token
  - `logout()`: Rimozione token e redirect a login
  - `getToken()`: Recupero token per richieste HTTP
  - `isAuthenticated()`: Verifica stato autenticazione
- **AuthInterceptor**: Allegamento automatico JWT token a tutte le richieste HTTP
- **AuthGuard**: Protezione rotte, redirect a `/login` per utenti non autenticati

#### 2. UI Components
- **LoginComponent**: Pagina di login standalone
  - Form con validazione (username, password)
  - Gestione errori (401 Unauthorized)
  - Redirect automatico dopo login
- **Logout Button**: Menu profilo nella toolbar
  - MatMenu con opzione "Logout"
  - Icona logout e label

#### 3. Routing
- **app.routes.ts**: 
  - Rotta `/login` pubblica
  - AuthGuard applicato a tutte le rotte protette (`/layout/*`)

### Files Creati

#### Backend
- `eCommerce.Inventory.Domain/Entities/User.cs`
- `eCommerce.Inventory.Application/DTOs/AuthDtos.cs` (LoginDto, RegisterDto, AuthResponseDto)
- `eCommerce.Inventory.Application/Interfaces/IAuthService.cs`
- `eCommerce.Inventory.Infrastructure/Services/AuthService.cs`
- `eCommerce.Inventory.Api/Controllers/AuthController.cs`
- `eCommerce.Inventory.Infrastructure/Migrations/*_AddUserAuthentication.cs`

#### Frontend
- `src/app/core/services/auth.service.ts`
- `src/app/core/interceptors/auth.interceptor.ts`
- `src/app/core/guards/auth.guard.ts`
- `src/app/features/auth/pages/login/login.component.ts`
- `src/app/features/auth/pages/login/login.component.html`
- `src/app/features/auth/pages/login/login.component.scss`

### Files Modificati

#### Backend
- `eCommerce.Inventory.Api/Program.cs` - JWT configuration, CORS policy
- `eCommerce.Inventory.Api/appsettings.json` - JwtSettings section
- `eCommerce.Inventory.Infrastructure/Persistence/ApplicationDbContext.cs` - DbSet<User>
- `eCommerce.Inventory.Infrastructure/Persistence/SeedData.cs` - Admin user seeding
- `eCommerce.Inventory.Application/Interfaces/IApplicationDbContext.cs` - Users property

#### Frontend
- `src/app/app.config.ts` - AuthInterceptor registration
- `src/app/app.routes.ts` - Login route, AuthGuard application
- `src/app/shared/layout/layout.component.ts` - Logout method, AuthService injection
- `src/app/shared/layout/layout.component.html` - Logout menu

### Bug Fixes

1. **Admin User Seeding**
   - Problema: Seeding bloccato da `context.Games.Any()` early return
   - Soluzione: Check indipendente `context.Users.Any(u => u.Username == "admin")`

2. **LoginComponent Module Declaration**
   - Problema: Componente standalone dichiarato in `auth.module.ts`
   - Soluzione: Rimosso `auth.module.ts`, LoginComponent è standalone

3. **CORS Configuration**
   - Problema: CORS non permetteva credenziali
   - Soluzione: `WithOrigins()` + `AllowCredentials()`

4. **API URL Port**
   - Problema: Frontend usava porta 5152 invece di 5155
   - Soluzione: Aggiornato `AuthService` con porta corretta

### Testing

- ✅ Login con utente admin (`admin` / `admin123`)
- ✅ Login con utente registrato (`testuser` / `test123`)
- ✅ Token JWT allegato automaticamente alle richieste
- ✅ Redirect a `/login` per utenti non autenticati
- ✅ Logout funzionante con rimozione token
- ✅ Backend compila senza errori
- ✅ Frontend compila senza errori

### Dipendenze Aggiunte

#### Backend
- `Microsoft.AspNetCore.Authentication.JwtBearer`
- `BCrypt.Net-Next`
- `System.IdentityModel.Tokens.Jwt`

#### Frontend
- Nessuna nuova dipendenza (usa Angular Material esistente)

### Prossimi Passi

- [ ] Deploy: Aggiornare Windows Service con modifiche auth
- [ ] Implementare refresh token mechanism
- [ ] Aggiungere gestione ruoli (Admin vs User)
- [ ] Implementare password reset
- [ ] Considerare 2FA per admin
- [ ] Test automatizzati per auth flow
