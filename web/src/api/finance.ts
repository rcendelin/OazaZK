import { apiClient, ApiError } from './client.ts';
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

export const exportFinancePdf = async (
  year: number,
  getToken: () => Promise<string | null>,
): Promise<void> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(
    `${baseUrl}/finance/export/pdf?year=${encodeURIComponent(String(year))}`,
    {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    },
  );

  if (!response.ok) {
    throw new ApiError(response.status, 'Export PDF se nezdařil');
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `hospodareni-${year}.pdf`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

export const exportFinanceExcel = async (
  year: number,
  getToken: () => Promise<string | null>,
): Promise<void> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(
    `${baseUrl}/finance/export/xlsx?year=${encodeURIComponent(String(year))}`,
    {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    },
  );

  if (!response.ok) {
    throw new ApiError(response.status, 'Export Excel se nezdařil');
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `hospodareni-${year}.xlsx`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};
