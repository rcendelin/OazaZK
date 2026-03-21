import { apiClient } from './client.ts';
import type {
  MonthlyReadingsResponse,
  ImportPreviewResponse,
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
