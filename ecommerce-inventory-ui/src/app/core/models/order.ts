export interface Order {
  id: number;
  cardTraderOrderId?: string;
  status: 'pending' | 'paid' | 'shipped' | 'delivered' | 'cancelled';
  totalPrice: number;
  createdAt: Date;
  updatedAt: Date;
}

export interface OrderItem {
  id: number;
  orderId: number;
  inventoryItemId: number;
  quantity: number;
  price: number;
}
