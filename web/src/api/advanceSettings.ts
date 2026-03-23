import { apiClient } from './client.ts';

export interface AdvanceSettingsData {
  waterPricePerM3: number;
  waterPriceValidFrom: string;
  waterPriceValidTo: string | null;
  monthlyAssociationFee: number;
  monthlyElectricityCost: number;
  electricityCoefficients: Record<string, number>;
  lossAllocationMethod: string;
}

export interface HouseAdvanceCalc {
  houseId: string;
  houseName: string;
  avgMonthlyConsumptionM3: number;
  lossShareM3: number;
  totalWaterM3: number;
  sharePercent: number;
  waterCostCzk: number;
  associationFeeCzk: number;
  electricityCoefficient: number;
  electricityCostCzk: number;
  totalAdvanceCzk: number;
}

export interface AdvanceCalculation {
  settings: {
    waterPricePerM3: number;
    waterPriceValidFrom: string;
    waterPriceValidTo: string | null;
    monthlyAssociationFee: number;
    monthlyElectricityCost: number;
    lossAllocationMethod: string;
  };
  mainMeterMonthlyConsumptionM3: number;
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
