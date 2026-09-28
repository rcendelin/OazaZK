import { apiClient } from './client.ts';
import type { AllocationMethod } from './costComponents.ts';

// Ownership periods and opening balances (T03). Days are `yyyy-MM-dd`.

export type OpeningBalanceType = 'MeterReading' | 'FundShare' | 'ComponentCredit';

export interface OwnershipPeriod {
  id: string;
  houseId: string;
  houseName: string;
  ownerName: string;
  contact: string | null;
  validFrom: string;
  validTo: string | null;
}

export interface OpeningBalance {
  key: string;
  type: OpeningBalanceType;
  houseId: string | null;
  houseName: string | null;
  componentId: string | null;
  componentName: string | null;
  meterId: string | null;
  meterNumber: string | null;
  ownershipPeriodId: string | null;
  date: string;
  /** m³ for MeterReading; CZK for FundShare (positive = the house has a credit) and ComponentCredit (negative = credit). */
  value: number;
  isEstimate: boolean;
  source: string;
  note: string | null;
  /** The date is fixed by an interim closing: changes need a reason (a correction). */
  locked: boolean;
}

export interface SaveOpeningBalance {
  type: OpeningBalanceType;
  houseId?: string;
  meterId?: string;
  componentId?: string;
  date: string;
  value: number;
  isEstimate: boolean;
  source: string;
  note?: string;
  reason?: string;
}

export interface ComponentCreditPreview {
  componentId: string;
  date: string;
  method: AllocationMethod;
  total: number;
  shares: { houseId: string; houseName: string; weight: number; amount: number }[];
}

const enc = encodeURIComponent;

export const getOwnershipPeriods = (): Promise<OwnershipPeriod[]> => apiClient.get('/ownership-periods');

export const startOwnership = (startDate: string): Promise<{ created: number; periods: OwnershipPeriod[] }> =>
  apiClient.post('/ownership-periods/start', { startDate });

export const getOpeningBalances = (): Promise<OpeningBalance[]> => apiClient.get('/opening-balances');

export const createOpeningBalance = (data: SaveOpeningBalance): Promise<OpeningBalance> =>
  apiClient.post('/opening-balances', data);

export const updateOpeningBalance = (key: string, data: SaveOpeningBalance): Promise<OpeningBalance> =>
  apiClient.put(`/opening-balances/${enc(key)}`, data);

export const deleteOpeningBalance = (key: string, reason?: string): Promise<void> =>
  apiClient.delete(`/opening-balances/${enc(key)}${reason ? `?reason=${enc(reason)}` : ''}`);

export const previewComponentCredit = (componentId: string, date: string, value: number): Promise<ComponentCreditPreview> =>
  apiClient.get(`/opening-balances/component-credit-preview?${new URLSearchParams({ componentId, date, value: String(value) }).toString()}`);
