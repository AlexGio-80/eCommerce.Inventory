# setup-iis.ps1
# Configures IIS for eCommerce Inventory
# REQUIRES ADMINISTRATOR PRIVILEGES

param (
    [string]$PublishPath = "C:\OSL\Sorgenti\Mio\eCommerce.Inventory\Publish",
    [string]$Domain = "inventory.local"
)

$ErrorActionPreference = "Stop"

# Check Admin
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole] "Administrator")) {
    Write-Warning "This script requires Administrator privileges!"
    Write-Warning "Please run PowerShell as Administrator and try again."
    exit 1
}

Write-Host "🚀 Configuring IIS for eCommerce Inventory..." -ForegroundColor Cyan

# 1. Ensure IIS is installed
$iisStatus = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebServer
if ($iisStatus.State -ne 'Enabled') {
    Write-Host "Installing IIS..." -ForegroundColor Yellow
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServer -All
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-ASPNET45 -All # Usually needed for hosting bundle interaction
}

# 2. Ensure .NET Hosting Bundle is installed (Check for ASP.NET Core Module)
if (-not (Test-Path "$env:ProgramFiles\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll")) {
    Write-Warning "⚠️  ASP.NET Core Hosting Bundle does not appear to be installed."
    Write-Warning "Please download and install it from: https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Warning "After installation, run this script again."
    # We continue, but it might fail
}

# 3. Ensure URL Rewrite is installed
if (-not (Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\IIS Extensions\URL Rewrite" -ErrorAction SilentlyContinue)) {
    Write-Warning "⚠️  IIS URL Rewrite Module is missing."
    Write-Warning "Angular routing requires URL Rewrite."
    Write-Warning "Please install it from: https://www.iis.net/downloads/microsoft/url-rewrite"
}

# 4. Setup App Pool
$poolName = "InventoryAppPool"
if (-not (Test-Path "IIS:\AppPools\$poolName")) {
    Write-Host "Creating AppPool: $poolName" -ForegroundColor Yellow
    New-WebAppPool -Name $poolName
    Set-ItemProperty "IIS:\AppPools\$poolName" -Name "managedRuntimeVersion" -Value "" # No Managed Code for .NET Core
} else {
    Write-Host "AppPool $poolName already exists." -ForegroundColor Gray
}

# 5. Setup Backend Site (API)
$siteName = "InventorySite"
$apiPath = Join-Path $PublishPath "api"
$uiPath = Join-Path $PublishPath "ui"

# We will host the UI as the root, and API as a sub-application or use URL Rewrite rules?
# Easier: Host UI as root, API as /api virtual directory/application.

if (-not (Test-Path "IIS:\Sites\$siteName")) {
    Write-Host "Creating Site: $siteName" -ForegroundColor Yellow
    New-Website -Name $siteName -Port 80 -HostHeader $Domain -PhysicalPath $uiPath -ApplicationPool $poolName
} else {
    Write-Host "Site $siteName already exists. Updating path..." -ForegroundColor Gray
    Set-ItemProperty "IIS:\Sites\$siteName" -Name "physicalPath" -Value $uiPath
}

# 6. Add API Application
$apiAppName = "api"
if (-not (Test-Path "IIS:\Sites\$siteName\$apiAppName")) {
    Write-Host "Creating API Application..." -ForegroundColor Yellow
    New-WebApplication -Name $apiAppName -Site $siteName -PhysicalPath $apiPath -ApplicationPool $poolName
}

# 7. Update Hosts File
$hostsPath = "$env:SystemRoot\System32\drivers\etc\hosts"
$hostsEntry = "127.0.0.1       $Domain"
$hostsContent = Get-Content $hostsPath
if ($hostsContent -notcontains $hostsEntry) {
    Write-Host "Adding $Domain to hosts file..." -ForegroundColor Yellow
    Add-Content -Path $hostsPath -Value "`r`n$hostsEntry"
}

# 8. Create web.config for Angular (URL Rewrite) if not exists
$webConfigPath = Join-Path $uiPath "web.config"
if (-not (Test-Path $webConfigPath)) {
    Write-Host "Creating web.config for Angular routing..." -ForegroundColor Yellow
    $webConfigContent = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <rewrite>
      <rules>
        <rule name="Angular Routes" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
            <add input="{REQUEST_URI}" pattern="^/api/" negate="true" />
          </conditions>
          <action type="Rewrite" url="/" />
        </rule>
      </rules>
    </rewrite>
    <staticContent>
        <mimeMap fileExtension=".json" mimeType="application/json" />
        <mimeMap fileExtension=".webmanifest" mimeType="application/manifest+json" />
    </staticContent>
  </system.webServer>
</configuration>
"@
    Set-Content -Path $webConfigPath -Value $webConfigContent
}

Write-Host "✅ IIS Setup Complete!" -ForegroundColor Green
Write-Host "Visit http://$Domain to see your application."
