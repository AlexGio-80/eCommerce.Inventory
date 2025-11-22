# Bulk Historical Orders Import Script
# Imports orders from Card Trader day-by-day from 2013-01-01 to today
# Usage: .\BulkImportOrders.ps1

$apiUrl = "http://localhost:5152/api/cardtrader/orders/sync"
$startDate = Get-Date "2013-01-01"
$endDate = Get-Date
$delayMs = 1000  # 1 second delay between requests to avoid rate limiting

Write-Host "=== Bulk Historical Orders Import ===" -ForegroundColor Cyan
Write-Host "Start Date: $($startDate.ToString('yyyy-MM-dd'))" -ForegroundColor Yellow
Write-Host "End Date: $($endDate.ToString('yyyy-MM-dd'))" -ForegroundColor Yellow
Write-Host "API URL: $apiUrl" -ForegroundColor Yellow
Write-Host ""

$currentDate = $startDate
$totalDays = ($endDate - $startDate).Days
$dayCount = 0
$successCount = 0
$errorCount = 0
$totalOrders = 0

while ($currentDate -le $endDate) {
    $dayCount++
    $fromDate = $currentDate.ToString("yyyy-MM-dd")
    $toDate = $currentDate.ToString("yyyy-MM-dd")
    
    $percentComplete = [math]::Round(($dayCount / $totalDays) * 100, 2)
    
    Write-Host "[$dayCount/$totalDays - $percentComplete%] Processing: $fromDate" -NoNewline
    
    try {
        $body = @{
            from = $fromDate
            to = $toDate
        } | ConvertTo-Json
        
        $response = Invoke-RestMethod -Uri $apiUrl -Method Post -Body $body -ContentType "application/json" -ErrorAction Stop
        
        $ordersCount = 0
        if ($response.message -match '\d+') {
            $ordersCount = [int]($response.message -replace '\D', '')
        }
        
        $totalOrders += $ordersCount
        $successCount++
        
        if ($ordersCount -gt 0) {
            Write-Host " ✓ $ordersCount orders" -ForegroundColor Green
        } else {
            Write-Host " ✓ No orders" -ForegroundColor Gray
        }
        
    } catch {
        $errorCount++
        Write-Host " ✗ ERROR: $($_.Exception.Message)" -ForegroundColor Red
    }
    
    # Move to next day
    $currentDate = $currentDate.AddDays(1)
    
    # Delay to avoid rate limiting
    if ($currentDate -le $endDate) {
        Start-Sleep -Milliseconds $delayMs
    }
}

Write-Host ""
Write-Host "=== Import Complete ===" -ForegroundColor Cyan
Write-Host "Total Days Processed: $dayCount" -ForegroundColor Yellow
Write-Host "Successful: $successCount" -ForegroundColor Green
Write-Host "Errors: $errorCount" -ForegroundColor Red
Write-Host "Total Orders Imported: $totalOrders" -ForegroundColor Cyan
Write-Host ""
