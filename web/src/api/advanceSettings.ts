import { apiClient } from './client.ts';

export interface HouseAdvanceOverride {
  waterAdvance: number;
  electricityAdvance: number;
  commonAdvance: number;
}

export interface AdvanceSettingsData {
  waterPricePerM3: number;
  waterPriceValidFrom: string;
  waterPriceValidTo: string | null;
  monthlyElectricityCost: number;
  monthlyCommonBaseFee: number;
  electricityCoefficients: Record<string, number>;
  houseOverrides: Record<string, HouseAdvanceOverride>;
  lossAllocationMethod: string;
}

export interface HouseAdvanceCalc {
  houseId: string;
  houseName: string;
  avgMonthlyM3: number;
  lossShareM3: number;
  totalWaterM3: number;
  sharePercent: number;
  electricityCoefficient: number;
  recommended: { water: number; electricity: number; common: number; total: number };
  actual: { water: number; electricity: number; common: number; total: number };
  hasOverride: boolean;
}

export interface AdvanceCalculation {
  settings: {
    waterPricePerM3: number;
    waterPriceValidFrom: string;
    waterPriceValidTo: string | null;
    monthlyElectricityCost: number;
    monthlyCommonBaseFee: number;
    lossAllocationMethod: string;
  };
  mainMeterMonthlyM3: number;
  totalIndividualMonthlyM3: number;
  monthlyLossM3: number;
  houses: HouseAdvanceCalc[];
}

export const getAdvanceSettings = (): Promise<AdvanceSettingsData> =>
  apiClient.get<AdvanceSettingsData>('/advance-settings');

export const updateAdvanceSettings = (data: AdvanceSettingsData): Promise<AdvanceSettingsData> =>
  apiClient.put<AdvanceSettingsData>('/advance-settings', data);

export const calculateAdvances = (): Promise<AdvanceCalculation> =>
  apiClient.get<AdvanceCalculation>('/advance-settings/calculate');
