import { apiClient } from './client.ts';
import type { AllocationMethod } from './costComponents.ts';

// Cost entries of components (T06). Days are `yyyy-MM-dd`.

export type CostEntryType = 'Advance' | 'Settlement' | 'OneOff';
export type PaidFrom = 'Bank' | 'SupplierCredit' | 'Cash' | 'Other';
export type AdvancePeriodicity = 'Monthly' | 'Quarterly' | 'HalfYearly' | 'Yearly';

export interface CostEntry {
  id: string;
  componentId: string;
  componentName: string;
  type: CostEntryType;
  periodFrom: string;
  periodTo: string;
  days: number;
  amount: number;
  quantityM3: number | null;
  supplier: string | null;
  documentId: string | null;
  paidFrom: PaidFrom;
  note: string | null;
  locked: boolean;
}

export interface SaveCostEntry {
  type: CostEntryType;
  periodFrom: string;
  periodTo: string;
  amount: number;
  quantityM3?: number;
  supplier?: string;
  documentId?: string;
  paidFrom: PaidFrom;
  note?: string;
  reason?: string;
}

export interface CostShare {
  houseId: string;
  houseName: string;
  weight: number;
  amount: number;
}

export interface CostEntryAllocation {
  entry: CostEntry;
  segments: { from: string; to: string; days: number; method: AllocationMethod; amount: number; shares: CostShare[] }[];
  houseTotals: CostShare[];
}

export const entryTypeLabels: Record<CostEntryType, string> = {
  Advance: 'Záloha',
  Settlement: 'Vyúčtování',
  OneOff: 'Jednorázový / faktura',
};

export const paidFromLabels: Record<PaidFrom, string> = {
  Bank: 'Z účtu',
  SupplierCredit: 'Z přeplatku u dodavatele',
  Cash: 'Hotově',
  Other: 'Jinak',
};

export const periodicityLabels: Record<AdvancePeriodicity, string> = {
  Monthly: 'měsíčně',
  Quarterly: 'čtvrtletně',
  HalfYearly: 'pololetně',
  Yearly: 'ročně',
};

const enc = encodeURIComponent;
const base = (componentId: string) => `/cost-components/${enc(componentId)}/entries`;

export const getCostEntries = (componentId: string, from?: string, to?: string): Promise<CostEntry[]> => {
  const params = new URLSearchParams();
  if (from) params.set('from', from);
  if (to) params.set('to', to);
  const query = params.toString();
  return apiClient.get(`${base(componentId)}${query ? `?${query}` : ''}`);
};

export const getCostEntryAllocation = (componentId: string, entryId: string): Promise<CostEntryAllocation> =>
  apiClient.get(`${base(componentId)}/${enc(entryId)}/allocation`);

export const createCostEntry = (componentId: string, data: SaveCostEntry): Promise<CostEntry> =>
  apiClient.post(base(componentId), data);

export const createRecurringAdvances = (componentId: string, data: {
  amount: number;
  periodicity: AdvancePeriodicity;
  from: string;
  to: string;
  supplier?: string;
  paidFrom: PaidFrom;
  note?: string;
}): Promise<CostEntry[]> => apiClient.post(`${base(componentId)}/recurring`, data);

export const updateCostEntry = (componentId: string, entryId: string, data: SaveCostEntry): Promise<CostEntry> =>
  apiClient.put(`${base(componentId)}/${enc(entryId)}`, data);

export const deleteCostEntry = (componentId: string, entryId: string, reason?: string): Promise<void> =>
  apiClient.delete(`${base(componentId)}/${enc(entryId)}${reason ? `?reason=${enc(reason)}` : ''}`);
