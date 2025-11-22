# Next Steps - Session 2025-11-22

## Status Summary (As of 2025-11-22)

**✅ Phase 3.3 COMPLETED**: Product Listing Creation with Pending Listings System
**✅ Phase 3.4 COMPLETED**: Orders Management (Manual Sync & Master-Detail View)
- Backend: Order/OrderItem entities, Repository, Controller, Sync Service
- Frontend: Orders List, Unprepared Items View, Sync Integration
- Features: Manual Sync, Order Completion Flag, Item Preparation Flag

## Immediate Next Steps

### Phase 3.5: Webhook Processing & Real-time Updates (PRIORITY: HIGH)

The next major feature to implement is real-time order tracking and inventory updates via Card Trader webhooks.

#### Backend Tasks
1. **Verify Webhook Infrastructure** (Already implemented in Phase 2)
   - ✅ WebhookSignatureVerificationService
   - ✅ ProcessCardTraderWebhookHandler
   - ✅ CardTraderWebhooksController
   - **TODO**: Test with real Card Trader webhook events

2. **Enhance Webhook Processing**
   - Add webhook event logging for debugging
   - Implement retry logic for failed webhook processing
   - Add webhook event history table

#### Frontend Tasks  
1. **Create WebSocket/SignalR Service** (if not using polling)
   - Real-time connection to backend for webhook events
   - Fallback to polling if WebSocket not available

2. **Order Status Monitor Component**
   - Display recent order updates in real-time
   - Show order timeline/history
   - Visual indicators for order state changes

3. **Inventory Auto-Update**
   - Listen for `product.sold` webhook events
   - Auto-decrement inventory quantities
   - Show notification when items sell

4. **Dashboard Real-time Stats**
   - Live sales counter
   - Recent activity feed
   - Today's revenue tracker

**Estimated Time**: 8-10 hours

---

### Phase 3.6: Bulk Operations & Advanced Features (PRIORITY: MEDIUM)

1. **Bulk Blueprint Navigation**
   - Previous/Next arrows to navigate cards within an expansion
   - Ordered by collector_number for sequential entry
   - Keyboard shortcuts (Arrow keys, Enter to submit)

2. **Batch Import**
   - CSV import for bulk inventory additions
   - Excel support
   - Validation and preview before import

3. **Advanced Filtering**
   - Filter pending listings by sync status, date range
   - Filter inventory by game, expansion, condition, foil
   - Saved filter presets

**Estimated Time**: 6-8 hours

---

### Phase 3.7: Reporting & Business Intelligence (PRIORITY: MEDIUM)

1. **Sales Analytics Dashboard**
   - Revenue charts (daily, weekly, monthly)
   - Top-selling cards
   - Profit margin analysis

2. **Inventory Analytics**
   - Stock levels by game/expansion
   - Slow-moving inventory identification
   - Price trend analysis

3. **Export Reports**
   - PDF/Excel export of sales reports
   - Tax reporting helpers
   - Inventory valuation reports

**Estimated Time**: 8-12 hours

---

### Phase 3.8: Polish & Optimization (PRIORITY: LOW)

1. **Performance Optimization**
   - Lazy loading for large lists
   - Virtualized scrolling for grids
   - Debounced search optimization

2. **UX Improvements**
   - Loading skeletons
   - Empty state illustrations
   - Improved error messages

3. **Mobile Responsiveness**
   - Mobile-optimized layout
   - Touch-friendly controls
   - PWA capabilities

**Estimated Time**: 4-6 hours

---

## Known Issues / Tech Debt

1. **None currently** - All identified issues from Phase 3.3 have been resolved

## Configuration Notes

- Card Trader API credentials stored in `appsettings.json`
- Frontend `.env` for API base URLs
- localStorage keys: `listing_defaults`

## Testing Checklist

Before starting new work:
- [x] Verify backend builds successfully
- [x] Verify frontend builds successfully  
- [ ] Test Pending Listings sync (create 2-3 items and sync)
- [ ] Test sync FROM Card Trader (verify InventoryItems populated)
- [ ] Verify "Save Defaults" toggle works correctly
- [ ] **Test Orders Manual Sync**
- [ ] **Test Order Completion Toggle**
- [ ] **Test Item Preparation Toggle**
- [ ] Check browser console for any errors
