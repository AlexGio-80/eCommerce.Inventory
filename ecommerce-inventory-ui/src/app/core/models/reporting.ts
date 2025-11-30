export interface SalesByExpansion {
  expansionName: string;
  totalRevenue: number;
  orderCount: number;
  percentage: number;
}

export interface ExpansionProfitability {
  expansionName: string;
  differenza: number;
  totaleVenduto: number;
  totaleAcquistato: number;
  percentualeDifferenza: number;
}
