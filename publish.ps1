# publish.ps1
# Automates the build and publish process for eCommerce Inventory
# REQUIRES ADMINISTRATOR PRIVILEGES (to stop/start IIS and Windows Services)

$ErrorActionPreference = "Stop"

$root = Get-Location
$publishDir = Join-Path $root "Publish"
$apiPublishDir = Join-Path $publishDir "api"
$uiPublishDir = Join-Path $publishDir "ui"

$apiProject = Join-Path $root "eCommerce.Inventory.Api"
$uiProject = Join-Path $root "ecommerce-inventory-ui"

$iisSiteName = "InventorySite"
$iisPoolName = "InventoryAppPool"
$serviceName = "eCommerce.Inventory"
$serviceExe = Join-Path $apiPublishDir "eCommerce.Inventory.Api.exe"

Write-Host "[*] Starting Deployment Build Process..." -ForegroundColor Cyan

# 0. Stop Services (IIS & Windows Service)
Write-Host "[*] Checking Service status..." -ForegroundColor Yellow

# Stop Windows Service
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -eq 'Running') {
    Write-Host "[-] Stopping Windows Service: $serviceName" -ForegroundColor Yellow
    Stop-Service -Name $serviceName -Force
    Start-Sleep -Seconds 2
}

# Stop IIS (using appcmd for robustness)
$appCmd = "$env:windir\system32\inetsrv\appcmd.exe"

if (Test-Path $appCmd) {
    Write-Host "[-] Stopping IIS Site: $iisSiteName" -ForegroundColor Yellow
    & $appCmd stop site "$iisSiteName" 2>$null
    
    Write-Host "[-] Stopping AppPool: $iisPoolName" -ForegroundColor Yellow
    & $appCmd stop apppool "$iisPoolName" 2>$null
    
    Start-Sleep -Seconds 2
}
else {
    Write-Warning "[!] appcmd.exe not found. Skipping IIS stop. File locking might occur."
}

# 1. Clean Publish Directory
if (Test-Path $publishDir) {
    Write-Host "[*] Cleaning publish directory..." -ForegroundColor Yellow
    Remove-Item $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $apiPublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $uiPublishDir -Force | Out-Null

# 2. Build Backend
Write-Host "[*] Building Backend (Release)..." -ForegroundColor Cyan
Push-Location $apiProject

dotnet publish -c Release -o $apiPublishDir
if ($LASTEXITCODE -ne 0) { 
    Pop-Location
    Write-Error "Backend build failed!"
    exit 1 
}

# Remove Development Settings
$devSettings = Join-Path $apiPublishDir "appsettings.Development.json"
if (Test-Path $devSettings) {
    Write-Host "[*] Removing appsettings.Development.json..." -ForegroundColor Yellow
    Remove-Item $devSettings -Force
}

# Note: appsettings.Production.json is automatically included by dotnet publish
# Make sure it exists in the project folder with your production secrets
$prodSettingsInPublish = Join-Path $apiPublishDir "appsettings.Production.json"
if (Test-Path $prodSettingsInPublish) {
    Write-Host "[+] appsettings.Production.json found in publish directory" -ForegroundColor Green
}
else {
    Write-Warning "[!] appsettings.Production.json NOT found in publish directory. Make sure it exists in the project folder."
}

Pop-Location

# 3. Build Frontend
Write-Host "[*] Building Frontend (Production)..." -ForegroundColor Cyan
Push-Location $uiProject

# Ensure dependencies are installed
if (-not (Test-Path "node_modules")) {
    Write-Host "[*] Installing NPM dependencies..." -ForegroundColor Yellow
    npm install
}

# Build Angular app
npm run build -- --configuration production
if ($LASTEXITCODE -ne 0) { 
    Pop-Location
    Write-Error "Frontend build failed!"
    exit 1 
}

# Copy artifacts
$distPathBrowser = Join-Path $uiProject "dist\ecommerce-inventory-ui\browser"
$distPathRoot = Join-Path $uiProject "dist\ecommerce-inventory-ui"

Write-Host "[*] Checking for artifacts..." -ForegroundColor DarkGray
if (Test-Path $distPathBrowser) {
    $distPath = $distPathBrowser
    Write-Host "[+] Found artifacts at: $distPathBrowser" -ForegroundColor Green
}
elseif (Test-Path $distPathRoot) {
    $distPath = $distPathRoot
    Write-Host "[+] Found artifacts at: $distPathRoot" -ForegroundColor Green
}
else {
    Pop-Location
    Write-Error "Could not find Angular dist folder. Checked:`n1. $distPathBrowser`n2. $distPathRoot"
    exit 1
}

Write-Host "[*] Copying UI artifacts to publish folder..." -ForegroundColor Yellow
Copy-Item "$distPath\*" $uiPublishDir -Recurse -Force
Pop-Location

# 4. Configure & Start Services

# Ensure logs directory exists
$logsDir = Join-Path $apiPublishDir "logs"
if (-not (Test-Path $logsDir)) {
    Write-Host "[*] Creating logs directory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $logsDir -Force | Out-Null
}

# Grant full control to NetworkService using icacls
Write-Host "[*] Setting permissions for NetworkService..." -ForegroundColor Yellow
try {
    icacls $apiPublishDir /grant "NT AUTHORITY\NETWORK SERVICE:(OI)(CI)F" /T /Q 2>$null | Out-Null
} catch {
    Write-Warning "[!] Could not set NetworkService permissions. Service may have issues writing logs."
}

# Install/Update Windows Service
if (-not $service) {
    Write-Host "[*] Creating Windows Service: $serviceName" -ForegroundColor Yellow
    New-Service -Name $serviceName `
        -BinaryPathName $serviceExe `
        -DisplayName "eCommerce Inventory API" `
        -Description "Backend API for eCommerce Inventory (runs on port 5152)" `
        -StartupType Automatic
    
    # Configure service to run as NetworkService
    Write-Host "[*] Configuring service to run as NetworkService..." -ForegroundColor Yellow
    sc.exe config $serviceName obj= "NT AUTHORITY\NetworkService" | Out-Null
}
else {
    # Update binary path and account
    $currentConfig = Get-WmiObject win32_service | Where-Object { $_.Name -eq $serviceName }
    if ($currentConfig.PathName -ne $serviceExe) {
        Write-Host "[*] Updating Windows Service Path..." -ForegroundColor Yellow
        sc.exe config $serviceName binPath= $serviceExe | Out-Null
    }
    
    # Ensure it runs as NetworkService
    if ($currentConfig.StartName -ne "NT AUTHORITY\NetworkService") {
        Write-Host "[*] Updating service account to NetworkService..." -ForegroundColor Yellow
        sc.exe config $serviceName obj= "NT AUTHORITY\NetworkService" | Out-Null
    }
}

# Configure IIS (create AppPool and Site if they don't exist)
if (Test-Path $appCmd) {
    # Check if AppPool exists
    $appPoolExists = & $appCmd list apppool "$iisPoolName" 2>$null
    if (-not $appPoolExists) {
        Write-Host "[*] Creating AppPool: $iisPoolName" -ForegroundColor Yellow
        & $appCmd add apppool /name:"$iisPoolName" /managedRuntimeVersion:"" /managedPipelineMode:"Integrated"
        & $appCmd set apppool "$iisPoolName" /processModel.identityType:NetworkService
    }
    
    # Check if Site exists
    $siteExists = & $appCmd list site "$iisSiteName" 2>$null
    if (-not $siteExists) {
        Write-Host "[*] Creating IIS Site: $iisSiteName" -ForegroundColor Yellow
        & $appCmd add site /name:"$iisSiteName" /physicalPath:"$uiPublishDir" /bindings:"http/*:80:inventory.local"
        & $appCmd set site "$iisSiteName" /[path='/'].applicationPool:"$iisPoolName"
    }
    else {
        # Update physical path in case it changed
        Write-Host "[*] Updating IIS Site physical path..." -ForegroundColor Yellow
        & $appCmd set site "$iisSiteName" /[path='/'].physicalPath:"$uiPublishDir"
    }
    
    # Start AppPool and Site
    Write-Host "[+] Starting AppPool: $iisPoolName" -ForegroundColor Green
    & $appCmd start apppool "$iisPoolName" 2>$null

    Write-Host "[+] Starting IIS Site: $iisSiteName" -ForegroundColor Green
    & $appCmd start site "$iisSiteName" 2>$null
}

# Start Windows Service (serves API on port 5152)
Write-Host "[+] Starting Windows Service: $serviceName" -ForegroundColor Green
Start-Service -Name $serviceName

# Wait a moment and check if service is running
Start-Sleep -Seconds 3
$serviceStatus = Get-Service -Name $serviceName
if ($serviceStatus.Status -eq 'Running') {
    Write-Host "[SUCCESS] Service is running!" -ForegroundColor Green
}
else {
    Write-Warning "[!] Service failed to start. Check Windows Event Viewer for details."
    Write-Host "You can check the service status with: Get-Service -Name '$serviceName'" -ForegroundColor DarkGray
}

Write-Host "[SUCCESS] Build and Deploy Complete!" -ForegroundColor Green
Write-Host "Frontend (UI): http://inventory.local" -ForegroundColor Cyan
Write-Host "Backend (API): http://localhost:5152" -ForegroundColor Cyan
Write-Host "Artifacts are located at: $publishDir" -ForegroundColor DarkGray
Write-Host "Logs are located at: $logsDir" -ForegroundColor DarkGray
