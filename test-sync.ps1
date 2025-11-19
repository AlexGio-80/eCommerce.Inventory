# Test sync per blueprints con filtro IsEnabled

$body = @{
    syncGames = $false
    syncExpansions = $false
    syncBlueprints = $true
    syncCategories = $false
    syncProperties = $false
} | ConvertTo-Json

Write-Host "Testing Blueprints Sync (only for enabled games)"
Write-Host "======================================"
Write-Host "Request body: $body"
Write-Host ""

try {
    $response = Invoke-WebRequest -Uri 'http://localhost:5152/api/cardtrader/sync' `
        -Method POST `
        -ContentType 'application/json' `
        -Body $body `
        -UseBasicParsing

    Write-Host "Status: $($response.StatusCode)"
    Write-Host ""
    Write-Host "Response:"
    Write-Host $response.Content | ConvertFrom-Json | ConvertTo-Json -Depth 10
}
catch {
    Write-Host "Error: $($_.Exception.Message)"
}
