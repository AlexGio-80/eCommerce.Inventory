<#
.SYNOPSIS
    Cambia la password di un utente dell'applicazione chiamando l'API.

.DESCRIPTION
    Le password si chiedono con Read-Host -AsSecureString: non compaiono a schermo, non
    finiscono nella cronologia di PowerShell e non vengono scritte su disco. Il cambio passa
    per POST /api/auth/change-password, che richiede la password attuale — quindi questo
    script non è una scorciatoia per scavalcarla, è solo il modo comodo di digitarla.

    L'API non espone alcun modo di reimpostare una password dimenticata: in quel caso l'unica
    strada è scrivere a mano un hash BCrypt nella colonna Users.PasswordHash.

.PARAMETER ApiUrl
    Base URL dell'API. Produzione: http://localhost:5152 (default). Sviluppo: http://localhost:5155.

.PARAMETER Username
    Utente di cui cambiare la password. Default: admin.

.EXAMPLE
    .\Cambia-PasswordAdmin.ps1

.EXAMPLE
    .\Cambia-PasswordAdmin.ps1 -ApiUrl http://localhost:5155 -Username admin
#>
[CmdletBinding()]
param(
    [string]$ApiUrl = "http://localhost:5152",
    [string]$Username = "admin"
)

$ErrorActionPreference = "Stop"

function ConvertFrom-SecureStringPlain {
    param([System.Security.SecureString]$Secure)

    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try {
        return [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        # Il testo in chiaro esiste solo per il tempo della richiesta HTTP.
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

Write-Host "Cambio password per l'utente '$Username' su $ApiUrl" -ForegroundColor Cyan

$currentSecure = Read-Host "Password attuale" -AsSecureString
$newSecure     = Read-Host "Password nuova (almeno 12 caratteri)" -AsSecureString
$confirmSecure = Read-Host "Ripeti la password nuova" -AsSecureString

$current = ConvertFrom-SecureStringPlain $currentSecure
$new     = ConvertFrom-SecureStringPlain $newSecure
$confirm = ConvertFrom-SecureStringPlain $confirmSecure

if ($new -ne $confirm) {
    Write-Host "Le due password nuove non coincidono. Niente è stato cambiato." -ForegroundColor Red
    exit 1
}

if ($new.Length -lt 12) {
    Write-Host "La password nuova deve essere lunga almeno 12 caratteri. Niente è stato cambiato." -ForegroundColor Red
    exit 1
}

try {
    $loginBody = @{ username = $Username; password = $current } | ConvertTo-Json
    $login = Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/auth/login" `
        -ContentType "application/json" -Body $loginBody

    $token = $login.data.token
    if (-not $token) {
        Write-Host "Login riuscito ma senza token nella risposta. Niente è stato cambiato." -ForegroundColor Red
        exit 1
    }

    $changeBody = @{ currentPassword = $current; newPassword = $new } | ConvertTo-Json
    Invoke-RestMethod -Method Post -Uri "$ApiUrl/api/auth/change-password" `
        -ContentType "application/json" -Headers @{ Authorization = "Bearer $token" } `
        -Body $changeBody | Out-Null

    Write-Host "Password aggiornata." -ForegroundColor Green
    Write-Host "Nota: i token gia' emessi restano validi fino a scadenza (7 giorni)." -ForegroundColor Yellow
    Write-Host "      Nel browser serve rifare il login con la password nuova." -ForegroundColor Yellow
}
catch {
    Write-Host "Errore: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ErrorDetails.Message) {
        Write-Host $_.ErrorDetails.Message -ForegroundColor Red
    }
    exit 1
}
finally {
    $current = $null
    $new = $null
    $confirm = $null
    [System.GC]::Collect()
}
