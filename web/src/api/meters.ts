import { apiClient } from './client.ts';
import type { WaterMeter, MeterType } from '../types/index.ts';

export const getMeters = (): Promise<WaterMeter[]> =>
  apiClient.get<WaterMeter[]>('/meters');

export const createMeter = (data: {
  meterNumber: string;
  name: string;
  type: MeterType;
  houseId: string | null;
}): Promise<WaterMeter> =>
  apiClient.post<WaterMeter>('/meters', data);

export const updateMeter = (id: string, data: {
  meterNumber: string;
  name: string;
  houseId: string | null;
}): Promise<WaterMeter> =>
  apiClient.put<WaterMeter>(`/meters/${encodeURIComponent(id)}`, data);
