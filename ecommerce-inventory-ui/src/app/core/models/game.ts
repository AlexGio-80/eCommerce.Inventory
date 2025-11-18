export interface Game {
  id: number;
  name: string;
  code: string;
  cardTraderId?: number;
  createdAt: Date;
  updatedAt: Date;
}
