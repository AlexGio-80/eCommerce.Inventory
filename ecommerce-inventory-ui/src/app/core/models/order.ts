export interface Order {
  id: number;
  cardTraderOrderId: number;
  code: string;
  transactionCode: string;
  buyerId: number;
  buyerUsername: string;
  state: string;
  paidAt?: Date;
  sentAt?: Date;
  sellerTotal: number;
  sellerFee: number;
  sellerSubtotal: number;
  shippingAddressJson: string;
  billingAddressJson: string;
  isCompleted: boolean;
  orderItems: OrderItem[];
}

export interface OrderItem {
  id: number;
  orderId: number;
  cardTraderId: number;
  productId: number;
  blueprintId: number;
  categoryId: number;
  gameId: number;
  name: string;
  expansionName: string;
  quantity: number;
  price: number;
  condition: string;
  language: string;
  isFoil: boolean;
  isSigned: boolean;
  isAltered: boolean;
  userDataField?: string;
  isPrepared: boolean;
}
