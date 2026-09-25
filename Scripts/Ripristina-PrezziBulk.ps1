<#
.SYNOPSIS
    Riporta al prezzo corretto le carte di bulk alzate per errore dall'autopricer fra il
    30/08 e il 25/09/2026. Intervento una tantum.

.DESCRIPTION
    Il motore di pricing scartava come implausibile il sovrapprezzo di Card Trader sul bulk
    (0,09 € su 0,10 €, rapporto 1,9, oltre il limite di 1,15) e confrontava l'incasso con i
    prezzi di vetrina senza conversione. Risultato: come INCASSO scriveva il prezzo che voleva
    ottenere IN VETRINA, quindi ogni carta stava circa 0,09 € sopra la posizione configurata.
    26.547 delle 26.585 inserzioni a 0,05 € del 30/08 sono state alzate.

    Il prezzo corretto si ricava senza chiamate al marketplace: è quello attuale meno il
    sovrapprezzo (0,09 € fino a 0,34 € in vetrina, 0,10 € sopra), mai sotto 0,05 €. Le carte a
    0,10–0,14 € tornano quindi a 0,05 €; le poche più alte scendono solo del sovrapprezzo,
    perché il loro mercato è davvero salito e a 0,05 € si venderebbero sottocosto.

    Tocca solo le inserzioni che:
      - erano a 0,05 € alla prima rilevazione dello storico (30/08, prezzatore nativo di CT);
      - hanno come prezzo attuale esattamente quello dell'ultima valutazione dell'autopricer,
        fatta con la conversione fallita (formulazione del motore vecchio): applicata, oppure
        "già allineato" quando confermava un prezzo scritto in una notte precedente.
    Così un ritocco manuale successivo non viene toccato.

    Va lanciato DOPO aver pubblicato la correzione del motore: con il motore vecchio in servizio
    la notturna rialzerebbe di nuovo le carte.

    Scrive su Card Trader con POST /products/bulk_update (asincrono, un job ogni blocco), poi
    rilegge l'export e allinea InventoryItems.ListingPrice: la notturna dell'autopricer parte
    prima che la sincronizzazione finisca, e deve trovare il prezzo nuovo già a database.

.PARAMETER Apply
    Senza questo switch lo script mostra solo cosa farebbe: nessuna scrittura.

.PARAMETER ConfigPath
    appsettings di produzione, da cui leggere token e connection string.

.PARAMETER BatchSize
    Inserzioni per job di bulk_update.

.EXAMPLE
    .\Ripristina-PrezziBulk.ps1              # prova, nessuna scrittura

.EXAMPLE
    .\Ripristina-PrezziBulk.ps1 -Apply
#>
[CmdletBinding()]
param(
    [switch]$Apply,
    [string]$ConfigPath,
    [int]$BatchSize = 1000
)

$ErrorActionPreference = "Stop"

# In Windows PowerShell 5.1 $PSScriptRoot non è ancora valorizzato nei default dei parametri.
if (-not $ConfigPath) { $ConfigPath = Join-Path $PSScriptRoot "..\Publish\api\appsettings.Production.json" }

# In Windows PowerShell 5.1 $PSScriptRoot non è ancora valorizzato nei default dei parametri.
if (-not $ConfigPath) { $ConfigPath = Join-Path $PSScriptRoot "..\Publishpippsettings.Production.json" }

$config = Get-Content $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
$baseUrl = $config.CardTraderApi.BaseUrl.TrimEnd('/')
$headers = @{ Authorization = "Bearer $($config.CardTraderApi.BearerToken)" }
$connectionString = $config.ConnectionStrings.DefaultConnection

function Invoke-Sql {
    param([string]$Sql, [hashtable]$Parameters = @{})

    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Sql
        $command.CommandTimeout = 300
        foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) }
        $table = New-Object System.Data.DataTable
        $table.Load($command.ExecuteReader())
        return , $table
    }
    finally {
        $connection.Close()
    }
}

# --- 1. Selezione -------------------------------------------------------------------------------

$selectionSql = @"
;WITH primo AS (
    SELECT CardTraderProductId, Price,
           ROW_NUMBER() OVER (PARTITION BY CardTraderProductId ORDER BY RecordedAt, Id) AS rn
    FROM PriceHistoryEntries
),
ultima AS (
    SELECT InventoryItemId, ProposedPrice, Outcome, Reason,
           ROW_NUMBER() OVER (PARTITION BY InventoryItemId ORDER BY Id DESC) AS rn
    FROM PriceChangeLogs
    WHERE InventoryItemId IS NOT NULL
)
SELECT ii.Id, ii.CardTraderProductId, ii.ListingPrice
FROM InventoryItems ii
JOIN primo p  ON p.CardTraderProductId = ii.CardTraderProductId AND p.rn = 1
JOIN ultima u ON u.InventoryItemId = ii.Id AND u.rn = 1
WHERE p.Price = 0.05
  AND ii.ListingPrice > 0.05
  AND u.Outcome IN (0, 2)   -- Applied, oppure NoChangeNeeded che conferma un prezzo applicato prima
  AND u.ProposedPrice = ii.ListingPrice
  AND u.Reason LIKE '%non ricavabile per questa carta: confronto fatto sul prezzo venditore%'
"@

$rows = Invoke-Sql $selectionSql

$targets = foreach ($row in $rows) {
    $current = [decimal]$row.ListingPrice
    $fee = if ($current -le 0.34) { 0.09 } else { 0.10 }
    $new = [Math]::Max(0.05, [Math]::Round($current - $fee, 2))
    [pscustomobject]@{
        InventoryItemId = [int]$row.Id
        ProductId       = [int]$row.CardTraderProductId
        Current         = $current
        New             = [decimal]$new
    }
}
$targets = @($targets | Where-Object { $_.New -lt $_.Current })

Write-Host ""
Write-Host "Inserzioni da riportare al prezzo corretto: $($targets.Count)"
$targets | Group-Object { "{0:0.00} -> {1:0.00}" -f $_.Current, $_.New } |
    Sort-Object Count -Descending | Select-Object -First 25 |
    ForEach-Object { Write-Host ("  {0,-14} {1,6}" -f $_.Name, $_.Count) }

if (-not $Apply) {
    Write-Host ""
    Write-Host "Modalità prova: nessuna scrittura. Rilanciare con -Apply per applicare." -ForegroundColor Yellow
    return
}

if ($targets.Count -eq 0) { return }

# --- 2. Scrittura su Card Trader ----------------------------------------------------------------

$totalOk = 0; $totalErrors = 0
for ($i = 0; $i -lt $targets.Count; $i += $BatchSize) {
    $batch = $targets[$i..([Math]::Min($i + $BatchSize, $targets.Count) - 1)]
    $body = @{ products = @($batch | ForEach-Object { @{ id = $_.ProductId; price = [double]$_.New } }) } |
        ConvertTo-Json -Depth 4 -Compress

    $job = Invoke-RestMethod -Method Post -Uri "$baseUrl/products/bulk_update" -Headers $headers `
        -ContentType "application/json" -Body $body
    Write-Host ("Blocco {0}-{1}: job {2}" -f ($i + 1), ($i + $batch.Count), $job.job)

    do {
        Start-Sleep -Seconds 5
        $status = Invoke-RestMethod -Uri "$baseUrl/jobs/$($job.job)" -Headers $headers
    } while ($status.state -in @("pending", "running"))

    $ok = [int]$status.stats.ok; $errors = [int]$status.stats.error
    $totalOk += $ok; $totalErrors += $errors
    Write-Host ("  stato {0}: ok {1}, avvisi {2}, errori {3}" -f $status.state, $ok, $status.stats.warning, $errors)
    if ($status.state -eq "unprocessable" -or $errors -gt 0) {
        $status.results | Where-Object { $_.errors } | Select-Object -First 5 | ForEach-Object {
            Write-Host ("    " + ($_ | ConvertTo-Json -Compress -Depth 5)) -ForegroundColor Red
        }
    }

    # Il limite di Card Trader è condiviso con il servizio che sta girando.
    Start-Sleep -Seconds 4
}

Write-Host ""
Write-Host "Card Trader: $totalOk aggiornate, $totalErrors errori"

# --- 3. Allineamento del database dall'export ---------------------------------------------------
# Si rilegge l'export invece di fidarsi del prezzo inviato: a database finisce ciò che Card
# Trader ha effettivamente registrato, anche per le righe andate in errore.

Write-Host "Rilettura dell'export per allineare il database..."
$export = Invoke-RestMethod -Uri "$baseUrl/products/export" -Headers $headers
$priceById = @{}
foreach ($p in $export) { $priceById[[int]$p.id] = [decimal]$p.price_cents / 100 }

$inv = [System.Globalization.CultureInfo]::InvariantCulture
$changes = @(foreach ($t in $targets) {
    if (-not $priceById.ContainsKey($t.ProductId)) { continue }
    $actual = $priceById[$t.ProductId]
    if ($actual -ne $t.Current) { "({0},{1})" -f $t.InventoryItemId, $actual.ToString("0.00", $inv) }
})

# Valori numerici prodotti dallo script stesso, quindi sicuri da comporre nel testo SQL.
for ($i = 0; $i -lt $changes.Count; $i += 900) {
    $values = $changes[$i..([Math]::Min($i + 900, $changes.Count) - 1)] -join ","
    [void](Invoke-Sql "UPDATE ii SET ListingPrice = v.p FROM InventoryItems ii JOIN (VALUES $values) AS v(id, p) ON v.id = ii.Id; SELECT @@ROWCOUNT")
}

Write-Host "Database: $($changes.Count) inserzioni allineate al prezzo di Card Trader" -ForegroundColor Green
