import { Blueprint } from './blueprint';

export interface InventoryItem {
  id: number;
  blueprintId: number;
  blueprint?: Blueprint;
  quantity: number;
  price: number;
  cardTraderProductId?: number;
  status: 'active' | 'inactive' | 'sold';
  createdAt: Date;
  updatedAt: Date;
}
