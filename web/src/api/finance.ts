import { apiClient, ApiError } from './client.ts';
import type {
  FinanceResponse,
  FinanceSummaryResponse,
  FinanceBalanceResponse,
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

export const getFinanceBalance = (): Promise<FinanceBalanceResponse> =>
  apiClient.get<FinanceBalanceResponse>('/finance/balance');

export interface FundBalanceResponse {
  commonContributions: number;
  extraordinaryCosts: number;
  fundBalance: number;
}

export const getFundBalance = (): Promise<FundBalanceResponse> =>
  apiClient.get<FundBalanceResponse>('/finance/fund');

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

export const uploadFinanceAttachment = async (
  id: string,
  file: File,
  getToken: () => Promise<string | null>,
): Promise<FinanceResponse> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(`${baseUrl}/finance/${encodeURIComponent(id)}/attachment`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/pdf',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
    },
    body: file,
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Nahrání přílohy selhalo' }));
    throw new ApiError(response.status, error.error || 'Nahrání přílohy selhalo');
  }

  return response.json() as Promise<FinanceResponse>;
};

export const downloadFinanceAttachment = async (
  id: string,
  filename: string,
  getToken: () => Promise<string | null>,
): Promise<void> => {
  const token = await getToken();
  const baseUrl = import.meta.env.VITE_API_BASE_URL || '/api';
  const response = await fetch(`${baseUrl}/finance/${encodeURIComponent(id)}/attachment`, {
    headers: token ? { Authorization: `Bearer ${token}` } : {},
  });

  if (!response.ok) {
    throw new ApiError(response.status, 'Stahování přílohy se nezdařilo');
  }

  const blob = await response.blob();
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
};

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
