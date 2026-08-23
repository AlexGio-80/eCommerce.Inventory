# Plan: Sealed Product Identification and Box Price Auto-Population

## Overview
Implement functionality to identify sealed products (booster boxes, cases, starter decks, etc.) by their category and auto-populate box prices in the inventory/analytics system.

## Key Findings from Investigation
- **Categories** are synced first (SyncCategoriesAsync), then **Blueprints** reference CategoryId
- **Sealed product categories** exist for each game:
  - Magic: 4 (Booster Boxes), 5 (Boosters), 7 (Starter Decks), 10 (Box Sets & Displays), 13 (Boxed Set)
  - Force of Will: 30, 31, 33, 34
  - Pokémon: 4576, 4580
  - Lorcana: 12821, 12825
  - And more...
- **Sealed categories** have "sealed" boolean property + minimal properties (mtg_language)
- **Regular card categories** have rich properties (rarity, condition, foil, language, collector_number)

## Tasks

### 1. Create Sealed Category Reference Data
- [ ] Add a static reference list of sealed category IDs per game in the Domain layer
- [ ] Create a helper method/extension to check if a category is a sealed product category
- [ ] Consider adding an `IsSealedProduct` property to Category entity or a service

### 2. Add Blueprint Identification Logic
- [ ] Add `IsSealedProduct` computed property to Blueprint entity (checks CategoryId against sealed list)
- [ ] Or create a BlueprintService method to identify sealed products
- [ ] Ensure this works efficiently for bulk operations

### 3. Integrate with Expansion Analytics/Profitability
- [ ] Modify expansion profitability calculation to detect sealed products
- [ ] Auto-populate box prices for sealed products based on:
  - Booster box = 36 boosters (typically)
  - Case = 6 booster boxes (typically)
  - Starter deck = single unit
- [ ] Update relevant API endpoints/services

### 4. Update UI Components (if needed)
- [ ] Show sealed product indicator in blueprint selector
- [ ] Display box/case pricing in product listings
- [ ] Update dashboard/analytics views

### 5. Testing
- [ ] Verify sealed product detection works for all games
- [ ] Test box price calculations
- [ ] Ensure backward compatibility with existing data

## Implementation Priority
1. Domain layer: sealed category reference + Blueprint.IsSealedProduct
2. Application layer: service methods for sealed product queries
3. Infrastructure: any repository updates needed
4. UI: display enhancements

## Questions for User
- Should box price calculation use fixed multipliers (36 boosters/box, 6 boxes/case) or be configurable per category?
- Where should box prices be displayed/used? (Dashboard, product listing, expansion analytics, all?)
- Should we add a new "BoxPrice" field to InventoryItem/Product or compute on-the-fly?
