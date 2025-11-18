export interface ApiResponse<T> {
  success: boolean;
  data?: T;
  message?: string;
  errors?: string[];
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface SyncProgress {
  status: 'idle' | 'running' | 'completed' | 'error';
  currentStep: string;
  progress: number;
}

export interface SyncResult {
  success: boolean;
  message: string;
  itemsSync: number;
  timestamp: Date;
}

export interface OrderUpdate {
  orderId: number;
  status: string;
  timestamp: Date;
}

export interface InventoryUpdate {
  productId: number;
  quantitySold: number;
  newPrice: number;
  timestamp: Date;
}
