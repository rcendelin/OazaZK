import { apiClient } from './client.ts';
import type {
  MonthlyReadingsResponse,
  ImportPreviewResponse,
  ChartResponse,
  ReadingResponse,
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

export const importReadingsFromClipboard = (
  text: string,
  readingDate: string,
): Promise<ImportPreviewResponse> =>
  apiClient.post<ImportPreviewResponse>('/readings/import/clipboard', {
    text,
    readingDate,
  });

/**
 * Confirms a previewed import. Stateless: the readings from the preview are sent
 * back and re-validated by the server; nothing is saved if any of them fails.
 */
export const confirmImport = (
  preview: ImportPreviewResponse,
): Promise<{ count: number }> =>
  apiClient.post<{ count: number }>('/readings/import/confirm', {
    readings: preview.rows.flatMap((row) =>
      Object.entries(row.meterValues).map(([meterId, value]) => ({
        meterId,
        readingDate: row.readingDate,
        value,
      })),
    ),
  });

export const createReading = (data: {
  meterId: string;
  readingDate: string;
  value: number;
}): Promise<void> =>
  apiClient.post<void>('/readings', data);

export const getAllReadings = (): Promise<ReadingResponse[]> =>
  apiClient.get<ReadingResponse[]>('/readings/all');

export const updateReading = (meterId: string, date: string, value: number, newDate?: string): Promise<void> =>
  apiClient.put<void>(`/readings/${encodeURIComponent(meterId)}/${encodeURIComponent(date)}`, {
    value,
    ...(newDate ? { newDate } : {}),
  });

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

export interface ReadingEstimate {
  meterId: string;
  targetDate: string;
  /** Null when the meter has no readings. */
  value: number | null;
  isEstimate: boolean;
  method: 'Exact' | 'Interpolated' | 'NearestBefore' | 'NearestAfter' | 'None';
  note: string;
}

/** Estimated meter state at a date (yyyy-MM-dd) — interpolation between readings (T04). Admin only. */
export const estimateReading = (meterId: string, date: string): Promise<ReadingEstimate> =>
  apiClient.get<ReadingEstimate>(
    `/readings/estimate?meterId=${encodeURIComponent(meterId)}&date=${encodeURIComponent(date)}`,
  );
