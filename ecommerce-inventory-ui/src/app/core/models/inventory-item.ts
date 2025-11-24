import { Blueprint } from './blueprint';

export interface InventoryItem {
  id: number;
  blueprintId: number;
  blueprint?: Blueprint;
  quantity: number;
  price: number;
  isFoil: boolean;
  isSigned: boolean;
  isAltered: boolean;
  cardTraderProductId?: number;
  status: 'active' | 'inactive' | 'sold';
  createdAt: Date;
  updatedAt: Date;
}
