/** Punto della serie storica del prezzo di una inserzione (vedi PriceHistoryEntry lato backend). */
export interface PriceHistoryPoint {
  recordedAt: string;
  price: number;
  quantity: number;
}

/**
 * Storico prezzi di una singola inserzione su Card Trader per una carta. Una carta può avere
 * più inserzioni (condizioni/lingue/foil diversi), quindi più serie per lo stesso blueprint.
 */
export interface PriceHistorySeries {
  cardTraderProductId: number;
  condition: string;
  language: string;
  isFoil: boolean;
  points: PriceHistoryPoint[];
}
