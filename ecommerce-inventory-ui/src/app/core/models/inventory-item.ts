import { Blueprint } from './blueprint';

export interface InventoryItem {
  id: number;
  blueprintId: number;
  blueprint?: Blueprint;
  quantity: number;
  price: number;
  purchasePrice?: number;
  listingPrice?: number;
  condition?: string;
  language?: string;
  isFoil: boolean;
  isSigned: boolean;
  isAltered: boolean;
  tag?: string;
  cardTraderProductId?: number;
  status: 'active' | 'inactive' | 'sold';
  createdAt: Date;
  updatedAt: Date;
}
