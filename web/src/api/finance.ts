import { apiClient } from './client.ts';
import type {
  FinanceResponse,
  FinanceSummaryResponse,
  CreateFinanceRequest,
  UpdateFinanceRequest,
} from '../types/index.ts';

export const getFinanceRecords = (
  year?: number,
  category?: string,
): Promise<FinanceResponse[]> => {
  const params = new URLSearchParams();
  if (year !== undefined) params.set('year', String(year));
  if (category) params.set('category', category);
  const qs = params.toString();
  return apiClient.get<FinanceResponse[]>(`/finance${qs ? `?${qs}` : ''}`);
};

export const getFinanceSummary = (
  year: number,
): Promise<FinanceSummaryResponse> =>
  apiClient.get<FinanceSummaryResponse>(
    `/finance/summary?year=${encodeURIComponent(String(year))}`,
  );

export const createFinanceRecord = (
  data: CreateFinanceRequest,
): Promise<FinanceResponse> =>
  apiClient.post<FinanceResponse>('/finance', data);

export const updateFinanceRecord = (
  id: string,
  data: UpdateFinanceRequest,
): Promise<FinanceResponse> =>
  apiClient.put<FinanceResponse>(
    `/finance/${encodeURIComponent(id)}`,
    data,
  );
