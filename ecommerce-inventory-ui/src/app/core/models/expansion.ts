export interface Expansion {
  id: number;
  gameId: number;
  name: string;
  code: string;
  cardTraderEmberId?: string;
  createdAt: Date;
  updatedAt: Date;
  averageCardValue?: number;
  totalMinPrice?: number;
  lastValueAnalysisUpdate?: Date;
}
