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
): Promise<void> =>
  apiClient.post<void>(`/billing-periods/${encodeURIComponent(periodId)}/close`, {
    lossAllocationMethod: method,
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
