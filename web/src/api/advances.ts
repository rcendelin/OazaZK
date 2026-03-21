import { apiClient } from './client.ts';
import type { AdvancePayment } from '../types/index.ts';

export const getAdvances = (
  houseId: string,
  year: number,
): Promise<AdvancePayment[]> =>
  apiClient.get<AdvancePayment[]>(
    `/advances?houseId=${encodeURIComponent(houseId)}&year=${encodeURIComponent(String(year))}`,
  );
