import { apiClient } from './client.ts';
import type { AllocationMethod } from './costComponents.ts';

// Water PVK and losses overview (T05). Days are `yyyy-MM-dd`.

export interface WaterHouse {
  houseId: string;
  houseName: string;
  consumptionM3: number;
  isEstimate: boolean;
  waterCost: number;
  lossCost: number;
}

export interface WaterInterval {
  from: string;
  to: string;
  days: number;
  mainConsumptionM3: number;
  housesConsumptionM3: number;
  lossM3: number;
  pricePerM3: number | null;
  invoicedAmount: number;
  invoicedM3: number;
  waterCost: number;
  lossCost: number;
  difference: number;
  allocated: boolean;
  warnings: string[];
  lossSegments: { from: string; to: string; days: number; method: AllocationMethod; amount: number; participants: number }[];
  houses: WaterHouse[];
}

export interface WaterSettlement {
  from: string;
  to: string;
  consumptionComponentName: string | null;
  lossComponentName: string | null;
  intervals: WaterInterval[];
  totals: WaterHouse[];
}

export const getWaterSettlement = (from: string, to: string): Promise<WaterSettlement> =>
  apiClient.get(`/water-settlement?${new URLSearchParams({ from, to }).toString()}`);
