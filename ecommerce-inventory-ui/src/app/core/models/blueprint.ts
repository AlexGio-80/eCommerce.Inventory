export interface Blueprint {
  id: number;
  cardTraderId: number;
  name: string;
  expansionId: number;
  gameId: number;
  imageUrl?: string;
  rarity?: string;
  version?: string;
  fixedProperties?: string;
  editableProperties?: string;
  expansion?: {
    id: number;
    name: string;
    code: string;
  };
  game?: {
    id: number;
    name: string;
  };
  createdAt: Date;
  updatedAt: Date;
}
