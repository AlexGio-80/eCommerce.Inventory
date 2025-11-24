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
  - **Fixed Tag Sync**: Corrected the API payload structure to send `tag` as a top-level field (instead of within `properties`), ensuring it is correctly recognized by Card Trader.

### Dashboard Performance
- **Fixed N+1 Query Issue**:
  - Identified excessive logging and performance overhead caused by `OrderRepository.GetOrdersWithItemsAsync` tracking thousands of `OrderItem` entities.
  - Added `.AsNoTracking()` to the query to disable change tracking for read-only dashboard data.
  - Significantly reduced memory usage and eliminated "Context started tracking..." logs.

## 🛠️ Infrastructure
- **Documentation**: Updated `ROADMAP.md` and cleaned up temporary files.

## ✨ Features

### Orders Grid Usability
- **Multi-Column Sorting**: Enabled sorting by multiple columns (Shift+Click) in "Orders to Prepare" grid.
- **Grid State Persistence**: Implemented saving of sort order, column dimensions, and visibility to `localStorage`.
- **Expansion Code**: Added "Exp. Code" column to the grid for better identification.
- **Image Preview**: Improved image preview to maximize size within the container.
- **Visual Improvements**:
  - **Condition**: Replaced text with colored badges (e.g., NM in green, PO in grey).
  - **Language**: Replaced text with flag icons (using `flag-icons` library via CDN).
  - **Foil**: Replaced "Yes/No" with a shiny star icon (`auto_awesome`).
- **Card Trader Link**: When marking an item as prepared, the application automatically opens the Card Trader product page in a new tab.
- **Grid Loading Fix**: Fixed an issue where the grid wouldn't display on first visit by forcing a grid refresh after data loads (using `setTimeout` with grid API update).
