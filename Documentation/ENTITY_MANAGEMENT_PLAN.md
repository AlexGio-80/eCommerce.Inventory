# Implementation Plan - Entity Management Pages

## Goal
Complete the entity management UI by implementing pages for Games, Categories, and Blueprints, following the same pattern established with the Expansions page.

## Current Status
✅ **Expansions Page** - Completed
- AG-Grid table with filtering/sorting
- Row selection with detail view
- Contextual sync action (Sync Blueprints)

✅ **Product Listing Creation** - Completed (Phase 3.3)
- Blueprint search with image preview
- Advanced multi-term search (name + expansion)
- Default values persistence (localStorage)
- Full CRUD for inventory items

## Remaining Pages

### 1. Games Management Page ✅ - Completed

#### Backend
- `GET /api/games` - List all games
- `GET /api/games/{id}` - Get single game
- `PUT /api/games/{id}` - Update game (for IsEnabled flag)

#### Frontend
- AG-Grid table showing: ID, Name, Code, IsEnabled
- Header form with:
  - Readonly fields: ID, Name, Code
  - **Editable toggle**: IsEnabled (Material slide-toggle)
  - Save button (enabled only when IsEnabled changes)
- Actions:
  - "Sync Expansions for this Game"
  - "Sync All Data for this Game" (Expansions + Blueprints)

---

### 2. Categories Management Page

#### Backend
- `GET /api/categories` - List all categories with game info
- `GET /api/categories/{id}` - Get single category with properties

#### Frontend
- AG-Grid table showing: ID, Name, Game Name
- Header form with readonly fields
- Expandable section showing Properties and their PossibleValues
- Actions:
  - "Sync Categories for Game" (if needed)

---

### 3. Blueprints Management Page

#### Backend
- `GET /api/blueprints` - List blueprints with pagination and filtering
  - Query params: `gameId`, `expansionId`, `search`, `page`, `pageSize`
- `GET /api/blueprints/{id}` - Get single blueprint with full details

#### Frontend
- AG-Grid table showing: ID, Name, Version, Expansion, Game, Rarity
- Advanced filters:
  - Game dropdown
  - Expansion dropdown (filtered by selected game)
  - Search by name
- Header form showing:
  - All blueprint properties (readonly)
  - Image preview (ImageUrl, BackImageUrl if available)
  - Fixed/Editable properties as JSON viewer
- No sync actions (blueprints are synced via Expansions)

---

## Design Pattern (Reusable)

All pages follow this structure:

```
┌─────────────────────────────────────┐
│  Page Title                         │
├─────────────────────────────────────┤
│  Header Card (Selected Item)       │
│  - Detail fields                    │
│  - Action buttons                   │
└─────────────────────────────────────┘
┌─────────────────────────────────────┐
│  AG-Grid Table                      │
│  - Filtering                        │
│  - Sorting                          │
│  - Pagination                       │
│  - Row selection                    │
└─────────────────────────────────────┘
```

### Shared Components to Create
1. **EntityDetailCard** - Reusable card for displaying selected entity
2. **SyncButton** - Reusable button with loading state
3. **AGGridWrapper** - Wrapper with common configuration

## Implementation Order

1. **Games Page** (Priority: High)
   - Needed for managing IsEnabled flag
   - Simple CRUD with one editable field

2. **Blueprints Page** (Priority: Medium)
   - Most complex (large dataset, multiple filters)
   - Useful for verifying sync results

3. **Categories Page** (Priority: Low)
   - Rarely modified
   - Mainly for reference

## Outstanding Issues to Debug
- None currently. Blueprint sync issue resolved.

## Verification Plan

After implementing all pages:
1. Test Games page: Toggle IsEnabled, verify database update
2. Test Blueprints page: Filter by Expansion 80, verify data appears
3. Test Categories page: View properties for MTG categories
4. Full sync test: Sync all entities, verify counts match API
