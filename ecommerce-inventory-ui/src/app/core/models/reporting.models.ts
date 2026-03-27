// Sales Metrics
export interface SalesMetrics {
    totalRevenue: number;
    totalOrders: number;
    averageOrderValue: number;
    growthPercentage: number;
    fromDate: Date;
    toDate: Date;
}

// Sales Chart Data
export interface SalesChartData {
    labels: string[];
    values: number[];
    groupBy: 'day' | 'week' | 'month';
}

// Top Product
export interface TopProduct {
    blueprintId: number;
    cardName: string;
    expansionName: string;
    gameName: string;
    quantitySold: number;
    totalRevenue: number;
    averagePrice: number;
}

// Sales By Game
export interface SalesByGame {
    gameName: string;
    totalRevenue: number;
    orderCount: number;
    percentage: number;
}

// Inventory Value
export interface InventoryValue {
    totalValue: number;
    totalItems: number;
    uniqueProducts: number;
    averageItemValue: number;
}

// Inventory Distribution
export interface InventoryDistribution {
    gameName: string;
    totalValue: number;
    itemCount: number;
    percentage: number;
}

// Slow Mover
export interface SlowMover {
    inventoryItemId: number;
    cardName: string;
    expansionName: string;
    quantity: number;
    listingPrice: number;
    daysInInventory: number;
    dateAdded: Date;
}

// Profitability Overview
export interface ProfitabilityOverview {
    totalRevenue: number;
    totalCost: number;
    grossProfit: number;
    profitMarginPercentage: number;
    roi: number;
    fromDate: Date;
    toDate: Date;
}

// Top Performer
export interface TopPerformer {
    blueprintId: number;
    cardName: string;
    expansionName: string;
    quantitySold: number;
    totalRevenue: number;
    totalCost: number;
    totalProfit: number;
    profitMarginPercentage: number;
}

// Date Range
export interface DateRange {
    from: Date;
    to: Date;
}

// Top Expansion Value
export interface TopExpansionValue {
    expansionId: number;
    expansionName: string;
    gameName: string;
    averageCardValue: number;
    totalMinPrice: number;
    lastUpdate?: Date;
}

// Tag Profitability
export interface TagProfitability {
    tag: string;
    totaleAcquistato: number;
    totaleVenduto: number;
    differenza: number;
    percentualeDifferenza: number;
    quantitaVenduta: number;
    qtaRimanente: number;
    valoreRimanente: number;
}

// Tag Expansion Profitability (breakdown per espansione dentro un tag)
export interface TagExpansionProfitability {
    expansionName: string;
    totaleAcquistato: number;
    totaleVenduto: number;
    differenza: number;
    percentualeDifferenza: number;
    quantitaVenduta: number;
    qtaRimanente: number;
    valoreRimanente: number;
}
