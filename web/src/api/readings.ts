import { apiClient } from './client.ts';
import type {
  MonthlyReadingsResponse,
  ImportPreviewResponse,
  ChartResponse,
} from '../types/index.ts';

export const getReadings = (
  year: number,
  month: number,
): Promise<MonthlyReadingsResponse> =>
  apiClient.get<MonthlyReadingsResponse>(
    `/readings?year=${encodeURIComponent(String(year))}&month=${encodeURIComponent(String(month))}`,
  );

export const importReadings = (
  file: File,
): Promise<ImportPreviewResponse> =>
  apiClient.uploadFile<ImportPreviewResponse>('/readings/import', file);

export const confirmImport = (
  sessionId: string,
): Promise<{ count: number }> =>
  apiClient.post<{ count: number }>('/readings/import/confirm', {
    importSessionId: sessionId,
  });

export const createReading = (data: {
  meterId: string;
  readingDate: string;
  value: number;
}): Promise<void> =>
  apiClient.post<void>('/readings', data);

export const updateReading = (meterId: string, date: string, value: number): Promise<void> =>
  apiClient.put<void>(`/readings/${encodeURIComponent(meterId)}/${encodeURIComponent(date)}`, { value });

export const getChartData = (
  houseId?: string,
  from?: string,
  to?: string,
): Promise<ChartResponse> => {
  const params = new URLSearchParams(
    Object.entries({ houseId, from, to }).filter(
      (entry): entry is [string, string] => entry[1] != null,
    ),
  );
  return apiClient.get<ChartResponse>(`/readings/chart?${params.toString()}`);
};
