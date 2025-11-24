# Changelog - 2025-11-24

## 🐛 Bug Fixes

### Product Listing UI
- **Fixed Missing Form Fields**: Restored `Quantity`, `Purchase Price`, `Selling Price`, `Tag`, and `Location` fields in `create-listing.component.html` which were accidentally truncated.
- **Fixed Navigation Arrows**: Restored "Previous/Next Card" navigation buttons in the blueprint details section.
- **Fixed Page Title**: Removed redundant "eCommerce Inventory" title from the toolbar.
- **Fixed Tag Persistence**: Updated `create-listing.component.ts` to correctly save and load the `Tag` field from `localStorage` defaults.

### Card Trader Integration
- **Fixed Price Suggestions**: 
  - Identified and fixed a critical bug where the **Local Blueprint ID** was being sent to the Card Trader API instead of the **Card Trader Blueprint ID**.
  - Updated `Blueprint` model in frontend to include `cardTraderId`.
  - Updated `create-listing.component.ts` to use `cardTraderId` for `loadMarketplaceStats`.
- **Fixed JSON Deserialization**:
  - Fixed `JsonException` in `CardTraderApiClient.GetMarketplaceProductsAsync`.
  - Updated deserialization logic to handle `Dictionary<string, List<ProductDto>>` response format instead of expecting a single object.
  - **Fixed Tag Sync**: Added `tag` field to the `properties` object in the product creation payload, ensuring it is correctly sent to Card Trader.

### Dashboard Performance
- **Fixed N+1 Query Issue**:
  - Identified excessive logging and performance overhead caused by `OrderRepository.GetOrdersWithItemsAsync` tracking thousands of `OrderItem` entities.
  - Added `.AsNoTracking()` to the query to disable change tracking for read-only dashboard data.
  - Significantly reduced memory usage and eliminated "Context started tracking..." logs.

## 🛠️ Infrastructure
- **Documentation**: Updated `ROADMAP.md` and cleaned up temporary files.
