export interface Blueprint {
  id: number;
  expansionId: number;
  cardName: string;
  cardTraderProductId?: number;
  rarity?: string;
  condition?: string;
  createdAt: Date;
  updatedAt: Date;
}
