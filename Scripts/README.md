# Script di utilità

- [`Cambia-PasswordAdmin.ps1`](#cambia-passwordadminps1) — cambia la password di un utente
- [`BulkImportOrders.ps1`](#bulk-orders-import-script) — importa gli ordini storici da Card Trader

---

## Cambia-PasswordAdmin.ps1

Cambia la password di un utente chiamando `POST /api/auth/change-password`. Le password si
digitano nascoste (`Read-Host -AsSecureString`), quindi non restano nella cronologia di
PowerShell né su disco. Serve la password attuale: lo script non la scavalca.

```powershell
cd Scripts
.\Cambia-PasswordAdmin.ps1                                        # produzione, utente admin
.\Cambia-PasswordAdmin.ps1 -ApiUrl http://localhost:5155          # sviluppo
```

Requisiti: API in esecuzione, minimo 12 caratteri per la password nuova.

I token già emessi restano validi fino a scadenza (7 giorni): dopo il cambio serve rifare il
login nel browser, ma una sessione aperta altrove non viene chiusa. Non esiste un recupero
password: se la password attuale è perduta, l'unica strada è scrivere a mano un hash BCrypt
in `Users.PasswordHash`.

---

# Bulk Orders Import Script

> **Da aggiornare prima del prossimo uso (2026-08-29).** Dal 2026-08-29 ogni endpoint dell'API
> richiede un token con ruolo `Admin`: questo script chiama `/api/cardtrader/orders/sync` senza
> autenticazione e riceverebbe `401`. Serve un login iniziale e l'header
> `Authorization: Bearer <token>` su ogni richiesta — vedi `Cambia-PasswordAdmin.ps1` per il
> giro di login. Non è stato modificato perché è un'importazione storica già eseguita.

## Overview
This PowerShell script imports historical orders from Card Trader API day-by-day from January 1, 2013 to today.

## Prerequisites
- Backend API must be running on `http://localhost:5152`
- PowerShell 5.1 or higher

## Usage

### Full Import (2013 to today)
```powershell
cd Scripts
.\BulkImportOrders.ps1
```

### Test with Small Range First (Recommended)
Before running the full import, test with a small date range by modifying the script:

```powershell
# Change these lines in the script:
$startDate = Get-Date "2024-11-01"  # Test with November 2024
$endDate = Get-Date "2024-11-30"
```

## Features
- **Day-by-day import**: Processes one day at a time to avoid overwhelming the API
- **Progress tracking**: Shows percentage complete and current date
- **Error handling**: Continues on errors and reports them at the end
- **Rate limiting**: 1-second delay between requests
- **Summary report**: Shows total days processed, successes, errors, and total orders imported

## Output Example
```
=== Bulk Historical Orders Import ===
Start Date: 2013-01-01
End Date: 2025-11-22
API URL: http://localhost:5152/api/cardtrader/orders/sync

[1/4700 - 0.02%] Processing: 2013-01-01 ✓ 5 orders
[2/4700 - 0.04%] Processing: 2013-01-02 ✓ No orders
[3/4700 - 0.06%] Processing: 2013-01-03 ✓ 12 orders
...

=== Import Complete ===
Total Days Processed: 4700
Successful: 4698
Errors: 2
Total Orders Imported: 15,234
```

## Performance
- **Estimated time**: ~1.3 hours for full import (4700 days × 1 second delay)
- **Can be interrupted**: Press `Ctrl+C` to stop. Re-run to continue from where it left off (orders already in DB will be updated, not duplicated)

## Notes
- The script uses POST requests with JSON body containing `from` and `to` dates
- Each request syncs orders for a single day
- Duplicate orders are handled by the backend (updates existing records)
- The backend has a limit of 1000 orders per request (should be sufficient for daily batches)

## Troubleshooting

### Backend not responding
Ensure the backend is running:
```powershell
cd eCommerce.Inventory.Api
dotnet run
```

### Rate limiting errors
Increase the delay in the script:
```powershell
$delayMs = 2000  # 2 seconds instead of 1
```

### Connection errors
Check that the API URL is correct and the backend is accessible at `http://localhost:5152`
