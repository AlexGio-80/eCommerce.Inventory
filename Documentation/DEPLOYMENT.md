# Deployment Options for eCommerce Inventory

Since this application is for personal use and currently in active development, we recommend a setup that balances **ease of update** with **stability**.

Here are three recommended approaches:

## Option 1: Scripted Local Run (Recommended for Development)
This is the simplest approach. You continue to run the application as you do now, but we automate the startup process with a single script.

**Pros:**
- Zero configuration.
- Easy to debug (console output visible).
- Instant updates (just restart the script).

**Cons:**
- Consoles must stay open.
- Not "always on" (stops when you close the window).

### Setup
Create a `start-inventory.ps1` file in the root folder:

```powershell
$root = Get-Location
$apiPath = Join-Path $root "eCommerce.Inventory.Api"
$uiPath = Join-Path $root "ecommerce-inventory-ui"

# Start Backend
Write-Host "Starting Backend..." -ForegroundColor Green
Start-Process dotnet -ArgumentList "run" -WorkingDirectory $apiPath -NoNewWindow

# Start Frontend
Write-Host "Starting Frontend..." -ForegroundColor Green
Start-Process npm -ArgumentList "run start" -WorkingDirectory $uiPath -NoNewWindow
```

## Option 2: Local IIS Hosting (Recommended for "Production-like" Feel)
Host the application on your local IIS (Internet Information Services). This makes the app run in the background as a proper web service.

**Pros:**
- Runs in the background (no open console windows).
- Starts automatically with Windows.
- Accessible via a custom domain (e.g., `http://inventory.local`).

**Cons:**
- Requires initial setup (installing IIS, Hosting Bundle).
- Updates require a "Publish" step (can be scripted).

### Setup Overview
1.  **Install IIS**: Enable "Internet Information Services" in Windows Features.
2.  **Install .NET Hosting Bundle**: Download and install the .NET 8 Hosting Bundle.
3.  **Publish Backend**: `dotnet publish -c Release -o C:\inetpub\wwwroot\inventory-api`
4.  **Build Frontend**: `ng build --configuration production` and copy `dist` to `C:\inetpub\wwwroot\inventory-ui`.
5.  **Configure IIS**: Create two sites (one for API, one for UI) or use URL Rewrite.

## Option 3: Docker Compose
Run the entire stack in isolated containers.

**Pros:**
- Clean environment (no dependencies installed on host).
- Consistent with server deployments.

**Cons:**
- Requires Docker Desktop (can be heavy).
- Slower development cycle (need to rebuild images).

---

## Recommendation
For your current phase ("active development but personal use"), **Option 1 (Scripted Run)** is best. It keeps the feedback loop tight.

When you want a more stable "installed" feel, move to **Option 2 (IIS)**. We can create a `publish.ps1` script to automate the update process for IIS.
