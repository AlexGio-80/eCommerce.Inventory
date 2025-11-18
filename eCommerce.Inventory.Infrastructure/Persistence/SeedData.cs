using eCommerce.Inventory.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace eCommerce.Inventory.Infrastructure.Persistence;

/// <summary>
/// Seed data for initial database population with test games, expansions, and blueprints.
/// Following SPECIFICATIONS: Use async operations and structured logging with Serilog
/// </summary>
public static class SeedData
{
    public static async Task InitializeAsync(ApplicationDbContext context, ILogger logger)
    {
        try
        {
            // Check if data already exists
            if (context.Games.Any())
            {
                logger.LogInformation("Database already seeded with games. Skipping seed data initialization.");
                return;
            }

            logger.LogInformation("Starting database seed with test data...");

            // Create Games
            var mtg = new Game
            {
                CardTraderId = 1,
                Name = "Magic: The Gathering",
                Code = "MTG"
            };

            var ygo = new Game
            {
                CardTraderId = 2,
                Name = "Yu-Gi-Oh!",
                Code = "YGO"
            };

            var pokemon = new Game
            {
                CardTraderId = 3,
                Name = "Pokémon",
                Code = "PKM"
            };

            context.Games.AddRange(mtg, ygo, pokemon);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {GameCount} games", 3);

            // Create Expansions for MTG
            var mtgExpansions = new List<Expansion>
            {
                new()
                {
                    CardTraderId = 101,
                    Name = "Dominaria United",
                    Code = "DMU",
                    GameId = mtg.Id
                },
                new()
                {
                    CardTraderId = 102,
                    Name = "The Brothers' War",
                    Code = "BRO",
                    GameId = mtg.Id
                }
            };

            // Create Expansions for Yu-Gi-Oh
            var ygoExpansions = new List<Expansion>
            {
                new()
                {
                    CardTraderId = 201,
                    Name = "Burst of Destiny",
                    Code = "BURST",
                    GameId = ygo.Id
                },
                new()
                {
                    CardTraderId = 202,
                    Name = "Duelist Nexus",
                    Code = "DNEX",
                    GameId = ygo.Id
                }
            };

            // Create Expansions for Pokémon
            var pokemonExpansions = new List<Expansion>
            {
                new()
                {
                    CardTraderId = 301,
                    Name = "Scarlet & Violet",
                    Code = "SV",
                    GameId = pokemon.Id
                },
                new()
                {
                    CardTraderId = 302,
                    Name = "Paldea Evolved",
                    Code = "PE",
                    GameId = pokemon.Id
                }
            };

            context.Expansions.AddRange(mtgExpansions);
            context.Expansions.AddRange(ygoExpansions);
            context.Expansions.AddRange(pokemonExpansions);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {ExpansionCount} expansions", 6);

            // Create Blueprints for MTG Dominaria United
            var dmBlueprints = new List<Blueprint>
            {
                new()
                {
                    CardTraderId = 1001,
                    Name = "Sheoldred, the Apocalypse",
                    Version = "Regular",
                    Rarity = "Mythic Rare",
                    ExpansionId = mtgExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 1002,
                    Name = "Jace, the Perfected Mind",
                    Version = "Regular",
                    Rarity = "Mythic Rare",
                    ExpansionId = mtgExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 1003,
                    Name = "Liliana of the Veil",
                    Version = "Regular",
                    Rarity = "Mythic Rare",
                    ExpansionId = mtgExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 1004,
                    Name = "Teferi, Temporal Pilgrim",
                    Version = "Regular",
                    Rarity = "Mythic Rare",
                    ExpansionId = mtgExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 1005,
                    Name = "Temporary Lockdown",
                    Version = "Regular",
                    Rarity = "Rare",
                    ExpansionId = mtgExpansions[0].Id
                }
            };

            // Create Blueprints for MTG The Brothers' War
            var broBlueprints = new List<Blueprint>
            {
                new()
                {
                    CardTraderId = 1101,
                    Name = "Urza, Lord High Artificer",
                    Version = "Regular",
                    Rarity = "Mythic Rare",
                    ExpansionId = mtgExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 1102,
                    Name = "Mishra, Eminent One",
                    Version = "Regular",
                    Rarity = "Mythic Rare",
                    ExpansionId = mtgExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 1103,
                    Name = "The Stasis Coffin",
                    Version = "Regular",
                    Rarity = "Rare",
                    ExpansionId = mtgExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 1104,
                    Name = "Gixian Skullcage",
                    Version = "Regular",
                    Rarity = "Rare",
                    ExpansionId = mtgExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 1105,
                    Name = "Fallaji Wayfarer",
                    Version = "Regular",
                    Rarity = "Uncommon",
                    ExpansionId = mtgExpansions[1].Id
                }
            };

            context.Blueprints.AddRange(dmBlueprints);
            context.Blueprints.AddRange(broBlueprints);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {BlueprintCount} blueprints for MTG expansions", dmBlueprints.Count + broBlueprints.Count);

            // Create Blueprints for Yu-Gi-Oh Burst of Destiny
            var burstBlueprints = new List<Blueprint>
            {
                new()
                {
                    CardTraderId = 2001,
                    Name = "Swordsoul Strategist Longyuan",
                    Version = "Regular",
                    Rarity = "Ultra Rare",
                    ExpansionId = ygoExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 2002,
                    Name = "Burner, Dragon Ruler of Sparks",
                    Version = "Regular",
                    Rarity = "Ultra Rare",
                    ExpansionId = ygoExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 2003,
                    Name = "Superheavy Samurai Stealth Ninja",
                    Version = "Regular",
                    Rarity = "Secret Rare",
                    ExpansionId = ygoExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 2004,
                    Name = "Swordsoul Grandmaster - Chixiao",
                    Version = "Regular",
                    Rarity = "Ultra Rare",
                    ExpansionId = ygoExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 2005,
                    Name = "Swordsoul Strategic Longyuan",
                    Version = "Regular",
                    Rarity = "Super Rare",
                    ExpansionId = ygoExpansions[0].Id
                }
            };

            // Create Blueprints for Yu-Gi-Oh Duelist Nexus
            var dnexBlueprints = new List<Blueprint>
            {
                new()
                {
                    CardTraderId = 2101,
                    Name = "Visas Starfrost",
                    Version = "Regular",
                    Rarity = "Ultra Rare",
                    ExpansionId = ygoExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 2102,
                    Name = "Firemind's Foresight",
                    Version = "Regular",
                    Rarity = "Ultra Rare",
                    ExpansionId = ygoExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 2103,
                    Name = "Mirror Synchron",
                    Version = "Regular",
                    Rarity = "Secret Rare",
                    ExpansionId = ygoExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 2104,
                    Name = "Promethean Perovskite",
                    Version = "Regular",
                    Rarity = "Rare",
                    ExpansionId = ygoExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 2105,
                    Name = "Testralyze",
                    Version = "Regular",
                    Rarity = "Super Rare",
                    ExpansionId = ygoExpansions[1].Id
                }
            };

            context.Blueprints.AddRange(burstBlueprints);
            context.Blueprints.AddRange(dnexBlueprints);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {BlueprintCount} blueprints for Yu-Gi-Oh expansions", burstBlueprints.Count + dnexBlueprints.Count);

            // Create Blueprints for Pokémon Scarlet & Violet
            var svBlueprints = new List<Blueprint>
            {
                new()
                {
                    CardTraderId = 3001,
                    Name = "Scarlet Pecharunt ex",
                    Version = "Regular",
                    Rarity = "Secret Rare",
                    ExpansionId = pokemonExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 3002,
                    Name = "Violet Pecharunt ex",
                    Version = "Regular",
                    Rarity = "Secret Rare",
                    ExpansionId = pokemonExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 3003,
                    Name = "Terapagos ex",
                    Version = "Regular",
                    Rarity = "Rare Holo",
                    ExpansionId = pokemonExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 3004,
                    Name = "Palafin ex",
                    Version = "Regular",
                    Rarity = "Rare Holo",
                    ExpansionId = pokemonExpansions[0].Id
                },
                new()
                {
                    CardTraderId = 3005,
                    Name = "Cyclizar",
                    Version = "Regular",
                    Rarity = "Uncommon",
                    ExpansionId = pokemonExpansions[0].Id
                }
            };

            // Create Blueprints for Pokémon Paldea Evolved
            var peBlueprints = new List<Blueprint>
            {
                new()
                {
                    CardTraderId = 3101,
                    Name = "Miraidon ex",
                    Version = "Regular",
                    Rarity = "Rare Holo",
                    ExpansionId = pokemonExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 3102,
                    Name = "Koraidon ex",
                    Version = "Regular",
                    Rarity = "Rare Holo",
                    ExpansionId = pokemonExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 3103,
                    Name = "Charizard ex",
                    Version = "Regular",
                    Rarity = "Secret Rare",
                    ExpansionId = pokemonExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 3104,
                    Name = "Ting-Lu ex",
                    Version = "Regular",
                    Rarity = "Rare Holo",
                    ExpansionId = pokemonExpansions[1].Id
                },
                new()
                {
                    CardTraderId = 3105,
                    Name = "Sprigatito",
                    Version = "Regular",
                    Rarity = "Common",
                    ExpansionId = pokemonExpansions[1].Id
                }
            };

            context.Blueprints.AddRange(svBlueprints);
            context.Blueprints.AddRange(peBlueprints);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {BlueprintCount} blueprints for Pokémon expansions", svBlueprints.Count + peBlueprints.Count);

            // Create sample InventoryItems
            var inventoryItems = new List<InventoryItem>
            {
                new()
                {
                    BlueprintId = dmBlueprints[0].Id,
                    CardTraderProductId = 50001,
                    PurchasePrice = 25.00m,
                    DateAdded = DateTime.UtcNow,
                    Quantity = 2,
                    ListingPrice = 35.00m,
                    Condition = "Mint",
                    Language = "English",
                    IsFoil = false,
                    IsSigned = false,
                    Location = "Shelf A1"
                },
                new()
                {
                    BlueprintId = dmBlueprints[1].Id,
                    CardTraderProductId = 50002,
                    PurchasePrice = 20.00m,
                    DateAdded = DateTime.UtcNow,
                    Quantity = 1,
                    ListingPrice = 28.00m,
                    Condition = "Near Mint",
                    Language = "English",
                    IsFoil = true,
                    IsSigned = false,
                    Location = "Shelf A2"
                },
                new()
                {
                    BlueprintId = broBlueprints[0].Id,
                    CardTraderProductId = 50003,
                    PurchasePrice = 18.00m,
                    DateAdded = DateTime.UtcNow,
                    Quantity = 3,
                    ListingPrice = 25.00m,
                    Condition = "Lightly Played",
                    Language = "English",
                    IsFoil = false,
                    IsSigned = false,
                    Location = "Shelf B1"
                },
                new()
                {
                    BlueprintId = burstBlueprints[0].Id,
                    CardTraderProductId = 50004,
                    PurchasePrice = 15.00m,
                    DateAdded = DateTime.UtcNow,
                    Quantity = 4,
                    ListingPrice = 22.00m,
                    Condition = "Mint",
                    Language = "English",
                    IsFoil = false,
                    IsSigned = false,
                    Location = "Shelf C1"
                },
                new()
                {
                    BlueprintId = svBlueprints[0].Id,
                    CardTraderProductId = 50005,
                    PurchasePrice = 12.00m,
                    DateAdded = DateTime.UtcNow,
                    Quantity = 5,
                    ListingPrice = 18.00m,
                    Condition = "Mint",
                    Language = "English",
                    IsFoil = false,
                    IsSigned = false,
                    Location = "Shelf D1"
                }
            };

            context.InventoryItems.AddRange(inventoryItems);
            await context.SaveChangesAsync();
            logger.LogInformation("Seeded {InventoryItemCount} inventory items", inventoryItems.Count);

            logger.LogInformation("Database seed completed successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database");
            throw;
        }
    }
}
