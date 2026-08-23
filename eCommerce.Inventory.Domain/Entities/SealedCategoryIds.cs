namespace eCommerce.Inventory.Domain.Entities;

/// <summary>
/// Static reference for Card Trader category IDs that represent sealed products (booster boxes, cases, starter decks, etc.)
/// These are used to identify Blueprint entities that are sealed products rather than single cards.
/// </summary>
public static class SealedCategoryIds
{
    // Magic: The Gathering (GameId from Card Trader)
    // 4 = Booster Boxes, 5 = Boosters, 7 = Starter Decks, 10 = Box Sets & Displays, 13 = Boxed Set
    public static readonly HashSet<int> Magic = new() { 4, 5, 7, 10, 13 };

    // Force of Will
    // 30 = Booster Boxes, 31 = Boosters, 33 = Starter Decks, 34 = Box Sets & Displays
    public static readonly HashSet<int> ForceOfWill = new() { 30, 31, 33, 34 };

    // Pokémon
    // 4576 = Booster Boxes, 4580 = Boosters
    public static readonly HashSet<int> Pokemon = new() { 4576, 4580 };

    // Lorcana
    // 12821 = Booster Boxes, 12825 = Boosters
    public static readonly HashSet<int> Lorcana = new() { 12821, 12825 };

    /// <summary>
    /// Checks if a category ID represents a sealed product for the given game
    /// </summary>
    /// <param name="gameId">Card Trader Game ID (1=MTG, 2=Force of Will, 3=Pokemon, 4=Lorcana)</param>
    /// <param name="categoryId">Card Trader Category ID</param>
    /// <returns>True if the category represents a sealed product</returns>
    public static bool IsSealedCategory(int gameId, int categoryId)
    {
        return gameId switch
        {
            1 => Magic.Contains(categoryId),      // MTG
            2 => ForceOfWill.Contains(categoryId), // Force of Will
            3 => Pokemon.Contains(categoryId),     // Pokémon
            4 => Lorcana.Contains(categoryId),     // Lorcana
            _ => false
        };
    }
}