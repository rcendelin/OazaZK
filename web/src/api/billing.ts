import { apiClient } from './client.ts';
import type {
  BillingPeriodResponse,
  CreateBillingPeriodRequest,
  SettlementPreviewResponse,
  SettlementResponse,
} from '../types/index.ts';

export const getBillingPeriods = (): Promise<BillingPeriodResponse[]> =>
  apiClient.get<BillingPeriodResponse[]>('/billing-periods');

export const createBillingPeriod = (
  data: CreateBillingPeriodRequest,
): Promise<BillingPeriodResponse> =>
  apiClient.post<BillingPeriodResponse>('/billing-periods', data);

export const updateBillingPeriod = (
  id: string,
  data: CreateBillingPeriodRequest,
): Promise<BillingPeriodResponse> =>
  apiClient.put<BillingPeriodResponse>(
    `/billing-periods/${encodeURIComponent(id)}`,
    data,
  );

export const calculateSettlement = (
  periodId: string,
  method: string,
): Promise<SettlementPreviewResponse> =>
  apiClient.get<SettlementPreviewResponse>(
    `/billing-periods/${encodeURIComponent(periodId)}/calculate?method=${encodeURIComponent(method)}`,
  );

export const closeBillingPeriod = (
  periodId: string,
  method: string,
  options?: {
    fundDrawAmount?: number;
    applyNewWaterPrice?: boolean;
    newWaterPriceValidFrom?: string;
  },
): Promise<void> =>
  apiClient.post<void>(`/billing-periods/${encodeURIComponent(periodId)}/close`, {
    lossAllocationMethod: method,
    fundDrawAmount: options?.fundDrawAmount ?? 0,
    applyNewWaterPrice: options?.applyNewWaterPrice ?? false,
    newWaterPriceValidFrom: options?.newWaterPriceValidFrom ?? null,
  });

export const getSettlements = (
  periodId: string,
): Promise<SettlementResponse[]> =>
  apiClient.get<SettlementResponse[]>(
    `/billing-periods/${encodeURIComponent(periodId)}/settlements`,
  );

export const getSettlementPdfUrl = (
  periodId: string,
  houseId: string,
): string => `/billing-periods/${encodeURIComponent(periodId)}/settlements/${encodeURIComponent(houseId)}/pdf`;

export const getAllSettlementsPdfUrl = (periodId: string): string =>
  `/billing-periods/${encodeURIComponent(periodId)}/pdf`;
