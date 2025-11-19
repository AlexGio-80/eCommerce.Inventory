# Script per importare Blueprints dal JSON al database SQL Server

param(
    [string]$JsonFilePath = ".\Features\Blueprints-Data-Examples.json",
    [string]$Server = "DEV-ALEX\MSSQLSERVER01",
    [string]$Database = "eCommerceInventory"
)

Write-Host "Blueprint Importer" -ForegroundColor Green
Write-Host "=================" -ForegroundColor Green
Write-Host ""

# Verifica file
if (-not (Test-Path $JsonFilePath)) {
    Write-Host "ERROR: File not found: $JsonFilePath" -ForegroundColor Red
    exit 1
}

Write-Host "Reading blueprints from: $JsonFilePath" -ForegroundColor Yellow

# Leggi JSON
$blueprintsJson = Get-Content $JsonFilePath | ConvertFrom-Json
Write-Host "Loaded $($blueprintsJson.Count) blueprints from JSON" -ForegroundColor Cyan

# Connessione al database
$connectionString = "Server=$Server;Database=$Database;Trusted_Connection=true;"
$connection = New-Object System.Data.SqlClient.SqlConnection
$connection.ConnectionString = $connectionString

try {
    $connection.Open()
    Write-Host "Connected to database: $Database" -ForegroundColor Green

    # Carica Games e Expansions dal database
    $cmd = New-Object System.Data.SqlClient.SqlCommand
    $cmd.Connection = $connection
    $cmd.CommandText = "SELECT Id, CardTraderId FROM Games"
    $reader = $cmd.ExecuteReader()
    $games = @{}
    while ($reader.Read()) {
        $games[$reader[1]] = $reader[0]  # CardTraderId => Id
    }
    $reader.Close()
    Write-Host "Loaded $($games.Count) games from database" -ForegroundColor Cyan

    $cmd.CommandText = "SELECT Id, CardTraderId FROM Expansions"
    $reader = $cmd.ExecuteReader()
    $expansions = @{}
    while ($reader.Read()) {
        $expansions[$reader[1]] = $reader[0]  # CardTraderId => Id
    }
    $reader.Close()
    Write-Host "Loaded $($expansions.Count) expansions from database" -ForegroundColor Cyan

    # Carica blueprints esistenti
    $cmd.CommandText = "SELECT CardTraderId FROM Blueprints"
    $reader = $cmd.ExecuteReader()
    $existingBlueprints = @{}
    while ($reader.Read()) {
        $existingBlueprints[$reader[0]] = $true
    }
    $reader.Close()
    Write-Host "Found $($existingBlueprints.Count) existing blueprints in database" -ForegroundColor Cyan

    # Importa blueprints
    $insertCount = 0
    $updateCount = 0
    $skipCount = 0

    Write-Host ""
    Write-Host "Processing blueprints..." -ForegroundColor Yellow

    foreach ($blueprint in $blueprintsJson) {
        $gameId = $games[$blueprint.game_id]
        $expansionId = $expansions[$blueprint.expansion_id]

        if (-not $gameId -or -not $expansionId) {
            $skipCount++
            continue
        }

        # Serialize properties to JSON
        $fixedPropsJson = if ($blueprint.fixed_properties) { ConvertTo-Json $blueprint.fixed_properties -Compress } else { $null }
        $editablePropsJson = if ($blueprint.editable_properties) { ConvertTo-Json $blueprint.editable_properties -Compress } else { $null }
        $cardMarketIdsJson = if ($blueprint.card_market_ids) { ConvertTo-Json $blueprint.card_market_ids -Compress } else { $null }

        # Extract rarity from fixed_properties
        $rarity = $null
        if ($blueprint.fixed_properties.mtg_rarity) {
            $rarity = $blueprint.fixed_properties.mtg_rarity
        } elseif ($blueprint.fixed_properties.rarity) {
            $rarity = $blueprint.fixed_properties.rarity
        }

        # Version
        $version = if ($blueprint.version) { $blueprint.version } else { "Regular" }

        # Controlla se esiste
        if ($existingBlueprints[$blueprint.id]) {
            # Update
            $cmd.CommandText = @"
UPDATE Blueprints
SET Name = @name, Version = @version, Rarity = @rarity, ImageUrl = @imageUrl,
    FixedProperties = @fixedProps, EditableProperties = @editableProps,
    CardMarketIds = @cardMarketIds, TcgPlayerId = @tcgPlayerId, ScryfallId = @scryfallId,
    BackImageUrl = @backImageUrl, UpdatedAt = @updatedAt
WHERE CardTraderId = @cardTraderId
"@
            $updateCount++
        } else {
            # Insert
            $cmd.CommandText = @"
INSERT INTO Blueprints (CardTraderId, Name, Version, GameId, ExpansionId, CategoryId,
    Rarity, ImageUrl, FixedProperties, EditableProperties, CardMarketIds,
    TcgPlayerId, ScryfallId, BackImageUrl, CreatedAt, UpdatedAt)
VALUES (@cardTraderId, @name, @version, @gameId, @expansionId, @categoryId,
    @rarity, @imageUrl, @fixedProps, @editableProps, @cardMarketIds,
    @tcgPlayerId, @scryfallId, @backImageUrl, @createdAt, @updatedAt)
"@
            $insertCount++
        }

        # Parameters
        $cmd.Parameters.Clear()
        $cmd.Parameters.AddWithValue("@cardTraderId", $blueprint.id) | Out-Null
        $cmd.Parameters.AddWithValue("@name", $blueprint.name) | Out-Null
        $cmd.Parameters.AddWithValue("@version", $version) | Out-Null
        $cmd.Parameters.AddWithValue("@gameId", $gameId) | Out-Null
        $cmd.Parameters.AddWithValue("@expansionId", $expansionId) | Out-Null
        $cmd.Parameters.AddWithValue("@categoryId", $blueprint.category_id) | Out-Null
        $cmd.Parameters.AddWithValue("@rarity", [System.DBNull]::Value) | Out-Null
        if ($rarity) { $cmd.Parameters["@rarity"].Value = $rarity }

        $cmd.Parameters.AddWithValue("@imageUrl", [System.DBNull]::Value) | Out-Null
        if ($blueprint.image_url) { $cmd.Parameters["@imageUrl"].Value = $blueprint.image_url }

        $cmd.Parameters.AddWithValue("@backImageUrl", [System.DBNull]::Value) | Out-Null
        if ($blueprint.back_image) { $cmd.Parameters["@backImageUrl"].Value = $blueprint.back_image }

        $cmd.Parameters.AddWithValue("@fixedProps", [System.DBNull]::Value) | Out-Null
        if ($fixedPropsJson) { $cmd.Parameters["@fixedProps"].Value = $fixedPropsJson }

        $cmd.Parameters.AddWithValue("@editableProps", [System.DBNull]::Value) | Out-Null
        if ($editablePropsJson) { $cmd.Parameters["@editableProps"].Value = $editablePropsJson }

        $cmd.Parameters.AddWithValue("@cardMarketIds", [System.DBNull]::Value) | Out-Null
        if ($cardMarketIdsJson) { $cmd.Parameters["@cardMarketIds"].Value = $cardMarketIdsJson }

        $cmd.Parameters.AddWithValue("@tcgPlayerId", [System.DBNull]::Value) | Out-Null
        if ($blueprint.tcg_player_id) { $cmd.Parameters["@tcgPlayerId"].Value = $blueprint.tcg_player_id }

        $cmd.Parameters.AddWithValue("@scryfallId", [System.DBNull]::Value) | Out-Null
        if ($blueprint.scryfall_id) { $cmd.Parameters["@scryfallId"].Value = $blueprint.scryfall_id }

        $cmd.Parameters.AddWithValue("@createdAt", [datetime]::UtcNow) | Out-Null
        $cmd.Parameters.AddWithValue("@updatedAt", [datetime]::UtcNow) | Out-Null

        $cmd.ExecuteNonQuery() | Out-Null
    }

    Write-Host ""
    Write-Host "Import completed!" -ForegroundColor Green
    Write-Host "  Inserted: $insertCount" -ForegroundColor Cyan
    Write-Host "  Updated: $updateCount" -ForegroundColor Cyan
    Write-Host "  Skipped: $skipCount" -ForegroundColor Yellow

    # Verifica conteggio finale
    $cmd.CommandText = "SELECT COUNT(*) FROM Blueprints"
    $totalCount = $cmd.ExecuteScalar()
    Write-Host ""
    Write-Host "Total blueprints in database: $totalCount" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}
finally {
    $connection.Close()
}
