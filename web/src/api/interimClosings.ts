import { apiClient } from './client.ts';

// Interim closings (T08). Saldo sign (X1): positive = přeplatek.

export type ClosingScope = 'All' | 'House';

export interface ClosingHouse {
  houseId: string;
  houseName: string;
  opening: number;
  payments: number;
  costs: number;
  saldo: number;
  currentSaldo: number;
  difference: number;
}

export interface InterimClosing {
  id: string;
  date: string;
  scope: ClosingScope;
  houseId: string | null;
  houseName: string | null;
  reason: string;
  createdByName: string | null;
  createdAt: string;
  totalSaldo: number;
  canDelete: boolean;
  houses: ClosingHouse[] | null;
}

const enc = encodeURIComponent;

export const getInterimClosings = (): Promise<InterimClosing[]> => apiClient.get('/interim-closings');

export const getInterimClosing = (id: string): Promise<InterimClosing> => apiClient.get(`/interim-closings/${enc(id)}`);

export const createInterimClosing = (data: { date: string; scope: ClosingScope; houseId?: string; reason: string }): Promise<InterimClosing> =>
  apiClient.post('/interim-closings', data);

export const deleteInterimClosing = (id: string, reason: string): Promise<void> =>
  apiClient.delete(`/interim-closings/${enc(id)}?reason=${enc(reason)}`);
