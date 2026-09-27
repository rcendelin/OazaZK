import { apiClient } from './client.ts';
import type { AdvancePayment } from '../types/index.ts';

export interface CreateAdvanceInput {
  houseId: string;
  year: number;
  month: number;
  waterAmount: number;
  electricityAmount: number;
  commonAmount: number;
  paymentDate: string; // ISO
}

export interface UpdateAdvanceInput {
  waterAmount: number;
  electricityAmount: number;
  commonAmount: number;
  paymentDate: string; // ISO
}

export interface CreateDoplatekInput {
  houseId: string;
  waterAmount: number;
  electricityAmount: number;
  commonAmount: number;
  paymentDate: string; // ISO
  note?: string;
}

export interface CreatePayoutInput {
  houseId: string;
  amount: number; // > 0, money returned to the household
  paymentDate: string; // ISO
  note?: string;
}

export const getAdvances = (houseId: string, year: number): Promise<AdvancePayment[]> =>
  apiClient.get<AdvancePayment[]>(
    `/advances?houseId=${encodeURIComponent(houseId)}&year=${encodeURIComponent(String(year))}`,
  );

/** All payments (advances + doplatky) across houses, optionally filtered by year. */
export const getAllAdvances = (year?: number): Promise<AdvancePayment[]> =>
  apiClient.get<AdvancePayment[]>(
    `/advances${year ? `?year=${encodeURIComponent(String(year))}` : ''}`,
  );

export const createAdvance = (data: CreateAdvanceInput): Promise<AdvancePayment> =>
  apiClient.post<AdvancePayment>('/advances', data);

export const updateAdvance = (
  houseId: string,
  yearMonth: string,
  data: UpdateAdvanceInput,
): Promise<AdvancePayment> =>
  apiClient.put<AdvancePayment>(
    `/advances/${encodeURIComponent(houseId)}/${encodeURIComponent(yearMonth)}`,
    data,
  );

export const createDoplatek = (data: CreateDoplatekInput): Promise<AdvancePayment> =>
  apiClient.post<AdvancePayment>('/advances/doplatek', data);

export const createPayout = (data: CreatePayoutInput): Promise<AdvancePayment> =>
  apiClient.post<AdvancePayment>('/advances/payout', data);

export const deletePayment = (houseId: string, rowKey: string): Promise<void> =>
  apiClient.delete(`/advances/${encodeURIComponent(houseId)}/${encodeURIComponent(rowKey)}`);
