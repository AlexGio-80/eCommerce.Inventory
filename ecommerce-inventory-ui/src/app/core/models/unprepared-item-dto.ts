export interface UnpreparedItemDto {
    id: number;
    name?: string;
    expansionName?: string;
    expansionCode?: string;
    condition?: string;
    language?: string;
    quantity: number;
    price: number;
    orderCode?: string;
    buyerUsername?: string;
    orderDate?: Date;
    isPrepared: boolean;
    imageUrl?: string;
    isFoil?: boolean;
    isSigned?: boolean;
    isAltered?: boolean;
    tag?: string;
    collectorNumber?: string;
}
