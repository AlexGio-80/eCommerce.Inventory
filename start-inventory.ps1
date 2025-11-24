# eCommerce Inventory - Startup Script
# This script starts both the backend API and the frontend Angular app

Write-Host "=== eCommerce Inventory - Starting Application ===" -ForegroundColor Cyan
Write-Host ""

# Get the script directory (project root)
$root = $PSScriptRoot
$apiPath = Join-Path $root "eCommerce.Inventory.Api"
$uiPath = Join-Path $root "ecommerce-inventory-ui"

# Verify paths exist
if (-not (Test-Path $apiPath)) {
    Write-Host "ERROR: Backend directory not found at $apiPath" -ForegroundColor Red
    exit 1
}

if (-not (Test-Path $uiPath)) {
    Write-Host "ERROR: Frontend directory not found at $uiPath" -ForegroundColor Red
    exit 1
}

# Start Backend API
Write-Host "[1/2] Starting Backend API..." -ForegroundColor Green
Write-Host "      Path: $apiPath" -ForegroundColor Gray
Write-Host "      URL: http://localhost:5000" -ForegroundColor Gray
Write-Host ""

try {
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$apiPath'; dotnet run" -WindowStyle Normal
    Write-Host "      Backend process started successfully" -ForegroundColor Green
} catch {
    Write-Host "      Failed to start backend: $_" -ForegroundColor Red
    exit 1
}

# Wait a bit for the backend to initialize
Start-Sleep -Seconds 2

# Start Frontend Angular App
Write-Host "[2/2] Starting Frontend Angular App..." -ForegroundColor Green
Write-Host "      Path: $uiPath" -ForegroundColor Gray
Write-Host "      URL: http://localhost:4200" -ForegroundColor Gray
Write-Host ""

try {
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$uiPath'; npm run start" -WindowStyle Normal
    Write-Host "      Frontend process started successfully" -ForegroundColor Green
} catch {
    Write-Host "      Failed to start frontend: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "=== Application Started Successfully ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Backend API:    http://localhost:5000/swagger" -ForegroundColor Yellow
Write-Host "Frontend UI:    http://localhost:4200" -ForegroundColor Yellow
Write-Host ""
Write-Host "Two new PowerShell windows have been opened." -ForegroundColor White
Write-Host "To stop the application, close both windows or press Ctrl+C in each." -ForegroundColor White
Write-Host ""
Write-Host "Press any key to exit this launcher..." -ForegroundColor Gray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
