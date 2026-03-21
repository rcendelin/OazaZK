import { apiClient } from './client.ts';
import type { Settlement } from '../types/index.ts';

export const getSettlements = (
  periodId: string,
): Promise<Settlement[]> =>
  apiClient.get<Settlement[]>(`/billing-periods/${encodeURIComponent(periodId)}/settlements`);
