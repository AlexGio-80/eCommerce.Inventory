# Script per abilitare Magic: The Gathering nel database

$connectionString = "Server=DEV-ALEX\MSSQLSERVER01;Database=eCommerceInventory;Trusted_Connection=true;"
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString
$connection.Open()

# Query to find all games
$cmd = New-Object System.Data.SqlClient.SqlCommand
$cmd.Connection = $connection
$cmd.CommandText = "SELECT Id, CardTraderId, Name, IsEnabled FROM Games ORDER BY Name"
$reader = $cmd.ExecuteReader()

Write-Host "Current Games Status:"
Write-Host "======================================"
while ($reader.Read()) {
    $id = $reader[0]
    $cardTraderId = $reader[1]
    $name = $reader[2]
    $isEnabled = if ($reader[3]) { "YES" } else { "NO" }
    Write-Host "$($id.ToString().PadRight(3)) | CT:$($cardTraderId.ToString().PadRight(4)) | $($name.PadRight(30)) | Enabled: $isEnabled"
}
$reader.Close()

# Enable Magic: The Gathering
Write-Host ""
Write-Host "Enabling Magic: The Gathering..."
$cmd.CommandText = "UPDATE Games SET IsEnabled = 1 WHERE Name LIKE '%Magic%'"
$rowsAffected = $cmd.ExecuteNonQuery()
Write-Host "Updated $rowsAffected game(s)"

# Show updated status
Write-Host ""
Write-Host "Enabled Games:"
Write-Host "======================================"
$cmd.CommandText = "SELECT Id, CardTraderId, Name FROM Games WHERE IsEnabled = 1 ORDER BY Name"
$reader = $cmd.ExecuteReader()
$enabledCount = 0
while ($reader.Read()) {
    $enabledCount++
    Write-Host "  [enabled] $($reader[2]) (ID: $($reader[0]))"
}
$reader.Close()
Write-Host "Total enabled: $enabledCount"

$connection.Close()
