# Blueprint Sync Debugging Notes

## Issue Summary
Blueprints are not being persisted to the database despite successful API calls.

## Timeline of Investigation

### Initial Problem (2025-11-20)
- Product sync failing with 14,685 failures
- Root cause: Missing blueprints in database
- Specific example: Expansion 80 (Commander 2015) has 0 blueprints

### API Investigation
**Test**: Manual API call to `https://api.cardtrader.com/api/v2/blueprints/export?expansion_id=80`
- ✅ Returns HTTP 200
- ✅ Returns JSON array with blueprint data
- ❌ Data not appearing in database after sync

### Deserialization Error Found
```
System.Text.Json.JsonException: The JSON value could not be converted to System.String. 
Path: $[362].back_image | LineNumber: 725
```

**Root Cause**: 
- `back_image` field can be either:
  - String (URL) for regular cards
  - Object (with image data) for double-faced cards
- DTO defined as `string`, causing failure on object values

### Fix Applied
1. Changed `BackImageUrl` type from `string` to `JsonElement?` in `CardTraderBlueprintDto`
2. Added `ExtractBackImageUrl()` helper method to handle both types
3. Re-added missing extraction methods (`ExtractCondition`, `ExtractLanguage`, `ExtractBooleanProperty`)

### Current Status (End of Session)
- ✅ Compilation successful
- ✅ No deserialization errors in logs
- ❌ Blueprints still not in database
- ⚠️ `ExpansionsController.SyncBlueprints` returns success but doesn't persist data

## Next Debugging Steps

### 1. Check ExpansionsController Implementation
Current code only fetches from API but doesn't save:
```csharp
var blueprintDtos = await _cardTraderApiService.SyncBlueprintsForExpansionAsync(
    expansion.CardTraderId, 
    cancellationToken);

// TODO: Map and save blueprints <-- MISSING!
return Ok(new { blueprintsFetched = blueprintDtos.Count() });
```

**Required fix**: Add mapping and database save logic

### 2. Verify Orchestrator Method
Check if `CardTraderSyncOrchestrator.UpsertBlueprintsAsync` exists and works correctly.

### 3. Add Detailed Logging
Add logs at each step:
- After API fetch
- After mapping
- After database save
- After transaction commit

### 4. Test Full Sync Flow
Compare behavior between:
- Full sync via `/api/cardtrader/sync` (works for other entities)
- Targeted sync via `/api/expansions/{id}/sync-blueprints` (doesn't persist)

## Code References

### Files Involved
- [CardTraderApiClient.cs](file:///c:/OSL/Sorgenti/Mio/eCommerce.Inventory/eCommerce.Inventory.Infrastructure/ExternalServices/CardTrader/CardTraderApiClient.cs) - API calls
- [CardTraderSyncOrchestrator.cs](file:///c:/OSL/Sorgenti/Mio/eCommerce.Inventory/eCommerce.Inventory.Infrastructure/ExternalServices/CardTrader/CardTraderSyncOrchestrator.cs) - Orchestration logic
- [CardTraderDtoMapper.cs](file:///c:/OSL/Sorgenti/Mio/eCommerce.Inventory/eCommerce.Inventory.Infrastructure/ExternalServices/CardTrader/Mappers/CardTraderDtoMapper.cs) - DTO to Entity mapping
- [ExpansionsController.cs](file:///c:/OSL/Sorgenti/Mio/eCommerce.Inventory/eCommerce.Inventory.Api/Controllers/ExpansionsController.cs) - New targeted sync endpoint

### Database Queries for Verification
```sql
-- Check if expansion exists
SELECT * FROM Expansions WHERE CardTraderId = 80;

-- Check blueprint count for expansion
SELECT COUNT(*) FROM Blueprints WHERE ExpansionId = 809;

-- Check all blueprints for Magic
SELECT COUNT(*) FROM Blueprints WHERE GameId = 1;
```

## Hypothesis
The `ExpansionsController.SyncBlueprints` endpoint is incomplete. It fetches data from the API but doesn't call the mapper or save to database. Need to integrate with `CardTraderSyncOrchestrator.UpsertBlueprintsAsync`.
