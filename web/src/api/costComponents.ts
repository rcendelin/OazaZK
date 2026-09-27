import { apiClient } from './client.ts';

// Cost components, allocation rules and participation (T02). Days are `yyyy-MM-dd`.

export type AllocationBasis = 'Metered' | 'CostEntries';
export type AllocationMethod = 'Metered' | 'Equal' | 'Ratio' | 'Percent';

export interface CostComponent {
  id: string;
  name: string;
  code: string;
  startDate: string;
  allocationBasis: AllocationBasis;
  active: boolean;
  note: string | null;
  currentMethod: AllocationMethod | null;
  currentParticipants: number;
}

export interface AllocationRule {
  id: string;
  validFrom: string;
  validTo: string | null;
  method: AllocationMethod;
  ratioSource: string | null;
  reason: string | null;
}

export interface Participation {
  id: string;
  houseId: string;
  houseName: string;
  validFrom: string;
  validTo: string | null;
  weight: number | null;
}

export interface CostComponentDetail {
  component: CostComponent;
  rules: AllocationRule[];
  participations: Participation[];
  lastClosedDay: string | null;
}

export interface AllocationSegment {
  from: string;
  to: string;
  days: number;
  method: AllocationMethod | null;
  participants: Participation[];
}

const base = '/cost-components';
const enc = encodeURIComponent;

export const methodLabels: Record<AllocationMethod, string> = {
  Metered: 'Podle odečtů',
  Equal: 'Rovným dílem',
  Ratio: 'Poměrem',
  Percent: 'Pevná procenta',
};

export const basisLabels: Record<AllocationBasis, string> = {
  Metered: 'Z odečtů vodoměrů',
  CostEntries: 'Z nákladových záznamů',
};

export const getCostComponents = (): Promise<CostComponent[]> => apiClient.get(base);

export const getCostComponent = (id: string): Promise<CostComponentDetail> => apiClient.get(`${base}/${enc(id)}`);

export const getSegments = (id: string, from: string, to: string): Promise<AllocationSegment[]> =>
  apiClient.get(`${base}/${enc(id)}/segments?${new URLSearchParams({ from, to }).toString()}`);

export const createCostComponent = (data: {
  name: string;
  code: string;
  startDate: string;
  allocationBasis: AllocationBasis;
  method?: AllocationMethod;
  note?: string;
}): Promise<CostComponent> => apiClient.post(base, data);

export const updateCostComponent = (id: string, data: { name: string; active: boolean; note?: string }): Promise<CostComponent> =>
  apiClient.put(`${base}/${enc(id)}`, data);

export const addRule = (id: string, data: { validFrom: string; method: AllocationMethod; ratioSource?: string; reason: string }): Promise<AllocationRule> =>
  apiClient.post(`${base}/${enc(id)}/rules`, data);

export const deleteRule = (id: string, ruleId: string, reason?: string): Promise<void> =>
  apiClient.delete(`${base}/${enc(id)}/rules/${enc(ruleId)}${reason ? `?reason=${enc(reason)}` : ''}`);

export const addParticipation = (id: string, data: {
  houseId: string;
  validFrom: string;
  validTo?: string;
  weight?: number;
  reason?: string;
}): Promise<Participation> => apiClient.post(`${base}/${enc(id)}/participations`, data);

export const endParticipation = (id: string, participationId: string, data: { validTo: string; reason?: string }): Promise<Participation> =>
  apiClient.post(`${base}/${enc(id)}/participations/${enc(participationId)}/end`, data);

export const deleteParticipation = (id: string, participationId: string, reason?: string): Promise<void> =>
  apiClient.delete(`${base}/${enc(id)}/participations/${enc(participationId)}${reason ? `?reason=${enc(reason)}` : ''}`);
