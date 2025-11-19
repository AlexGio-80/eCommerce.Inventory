using System.Text.Json;
using eCommerce.Inventory.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Persistence;

/// <summary>
/// Utility class for seeding blueprint data from JSON files
/// Used for testing and development purposes
/// </summary>
public class BlueprintSeeding
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<BlueprintSeeding> _logger;

    public BlueprintSeeding(ApplicationDbContext context, ILogger<BlueprintSeeding> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Import blueprints from JSON file
    /// Expects a JSON array of blueprint objects
    /// </summary>
    public async Task ImportBlueprintsFromJsonAsync(
        string jsonFilePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(jsonFilePath))
            {
                _logger.LogWarning("Blueprint JSON file not found at {FilePath}", jsonFilePath);
                return;
            }

            _logger.LogInformation("Reading blueprints from {FilePath}", jsonFilePath);

            var jsonContent = await File.ReadAllTextAsync(jsonFilePath, cancellationToken);
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var blueprintDtos = JsonSerializer.Deserialize<List<BlueprintJsonDto>>(jsonContent, options)
                ?? new List<BlueprintJsonDto>();

            _logger.LogInformation("Parsed {BlueprintCount} blueprints from JSON", blueprintDtos.Count);

            // Get all games for lookup
            var games = await _context.Games.ToListAsync(cancellationToken);
            var expansions = await _context.Expansions.ToListAsync(cancellationToken);

            int insertCount = 0, updateCount = 0, skipCount = 0;

            _logger.LogInformation("Starting to process {BlueprintCount} blueprints. Games in DB: {GameCount}, Expansions in DB: {ExpansionCount}",
                blueprintDtos.Count, games.Count, expansions.Count);

            foreach (var dto in blueprintDtos)
            {
                try
                {
                    // Find the expansion by CardTraderId (which matches expansion_id in JSON)
                    var expansion = expansions.FirstOrDefault(e => e.CardTraderId == dto.ExpansionId);
                    if (expansion == null)
                    {
                        skipCount++;
                        _logger.LogDebug("Skipping blueprint {BlueprintId} - expansion {ExpansionId} not found",
                            dto.Id, dto.ExpansionId);
                        continue;
                    }

                    // Find the game
                    var game = games.FirstOrDefault(g => g.CardTraderId == dto.GameId);
                    if (game == null)
                    {
                        skipCount++;
                        _logger.LogDebug("Skipping blueprint {BlueprintId} - game {GameId} not found",
                            dto.Id, dto.GameId);
                        continue;
                    }

                    // Check if blueprint already exists
                    var existingBlueprint = await _context.Blueprints
                        .FirstOrDefaultAsync(b => b.CardTraderId == dto.Id, cancellationToken);

                    var blueprint = new Blueprint
                    {
                        CardTraderId = dto.Id,
                        Name = dto.Name,
                        Version = string.IsNullOrWhiteSpace(dto.Version) ? "Regular" : dto.Version,
                        GameId = game.Id,
                        ExpansionId = expansion.Id,
                        CategoryId = dto.CategoryId,
                        ImageUrl = dto.ImageUrl,
                        BackImageUrl = dto.BackImageUrl,
                        TcgPlayerId = dto.TcgPlayerId,
                        ScryfallId = dto.ScryfallId,
                        Rarity = ExtractRarity(dto.FixedProperties),
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };

                    // Serialize complex properties as JSON
                    if (dto.FixedProperties != null && dto.FixedProperties.Any())
                    {
                        blueprint.FixedProperties = JsonSerializer.Serialize(dto.FixedProperties);
                    }

                    if (dto.EditableProperties != null && dto.EditableProperties.Any())
                    {
                        blueprint.EditableProperties = JsonSerializer.Serialize(dto.EditableProperties);
                    }

                    if (dto.CardMarketIds != null && dto.CardMarketIds.Any())
                    {
                        blueprint.CardMarketIds = JsonSerializer.Serialize(dto.CardMarketIds);
                    }

                    if (existingBlueprint == null)
                    {
                        _context.Blueprints.Add(blueprint);
                        insertCount++;
                        _logger.LogDebug("Added blueprint {BlueprintId} ({BlueprintName})", dto.Id, dto.Name);
                    }
                    else
                    {
                        // Update existing
                        blueprint.Id = existingBlueprint.Id;
                        _context.Blueprints.Update(blueprint);
                        updateCount++;
                        _logger.LogDebug("Updated blueprint {BlueprintId} ({BlueprintName})", dto.Id, dto.Name);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing blueprint {BlueprintId}", dto.Id);
                    skipCount++;
                }
            }

            if (insertCount > 0 || updateCount > 0)
            {
                _logger.LogInformation("Saving {BlueprintCount} blueprints to database (Insert: {InsertCount}, Update: {UpdateCount})",
                    insertCount + updateCount, insertCount, updateCount);
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Successfully saved blueprints to database");
            }
            else
            {
                _logger.LogInformation("No blueprints to save");
            }

            _logger.LogInformation("Blueprint import completed. Inserted: {InsertCount}, Updated: {UpdateCount}, Skipped: {SkipCount}",
                insertCount, updateCount, skipCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error importing blueprints from JSON file");
            throw;
        }
    }

    /// <summary>
    /// Extract rarity from fixed properties dictionary
    /// Looks for mtg_rarity, yu_gi_oh_rarity, or rarity keys
    /// </summary>
    private string ExtractRarity(Dictionary<string, object> fixedProperties)
    {
        if (fixedProperties == null || !fixedProperties.Any())
        {
            return null;
        }

        var rarityKey = fixedProperties.Keys
            .FirstOrDefault(k => k.Equals("mtg_rarity", StringComparison.OrdinalIgnoreCase) ||
                                 k.Equals("yu_gi_oh_rarity", StringComparison.OrdinalIgnoreCase) ||
                                 k.Equals("pokemon_rarity", StringComparison.OrdinalIgnoreCase) ||
                                 k.Equals("rarity", StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrEmpty(rarityKey) && fixedProperties[rarityKey] != null)
        {
            return fixedProperties[rarityKey].ToString();
        }

        return null;
    }
}

/// <summary>
/// DTO for deserializing blueprint JSON
/// Maps from Card Trader API response format
/// </summary>
public class BlueprintJsonDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Version { get; set; }
    public int GameId { get; set; }
    public int CategoryId { get; set; }
    public int ExpansionId { get; set; }
    public Dictionary<string, object> FixedProperties { get; set; }
    public List<object> EditableProperties { get; set; }
    public List<int> CardMarketIds { get; set; }
    public int? TcgPlayerId { get; set; }
    public string ScryfallId { get; set; }
    public string ImageUrl { get; set; }
    public string BackImageUrl { get; set; }
}
