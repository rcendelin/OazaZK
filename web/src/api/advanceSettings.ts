import { apiClient } from './client.ts';

/** Admin-set monthly advance of one house (Kč per component). */
export interface HouseAdvanceOverride {
  waterAdvance: number;
  electricityAdvance: number;
  commonAdvance: number;
}

/** Only per-house overrides remain; members receive an empty map. */
export interface AdvanceSettingsData {
  houseOverrides: Record<string, HouseAdvanceOverride>;
}

export interface AdvanceAmounts {
  water: number;
  electricity: number;
  common: number;
  total: number;
}

export interface HouseAdvanceCalc {
  houseId: string;
  houseName: string;
  /** Costs allocated to the house by the ledger over the period. */
  costsInPeriod: AdvanceAmounts;
  /** costsInPeriod ÷ months, rounded to whole Kč. */
  recommended: AdvanceAmounts;
  /** Admin override if set, otherwise the recommendation. */
  actual: AdvanceAmounts;
  hasOverride: boolean;
}

export interface AdvanceCalculation {
  /** yyyy-MM-dd */
  from: string;
  /** yyyy-MM-dd */
  to: string;
  months: number;
  houses: HouseAdvanceCalc[];
}

export const getAdvanceSettings = (): Promise<AdvanceSettingsData> =>
  apiClient.get<AdvanceSettingsData>('/advance-settings');

export const updateAdvanceSettings = (data: AdvanceSettingsData): Promise<AdvanceSettingsData> =>
  apiClient.put<AdvanceSettingsData>('/advance-settings', data);

export const calculateAdvances = (): Promise<AdvanceCalculation> =>
  apiClient.get<AdvanceCalculation>('/advance-settings/calculate');
